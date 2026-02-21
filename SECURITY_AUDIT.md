# Security Audit Report — NANOVNACSharp

**Date:** 2026-02-21
**Scope:** Full codebase static analysis
**Auditor:** Automated security review
**Version audited:** 1.0.0.0

---

## Executive Summary

NANOVNACSharp is a .NET Framework 4.8 class library for interfacing with a NanoVNA H4 vector network analyzer over USB serial. It is intended for integration with NI TestStand automated test platforms. The audit identified **2 high**, **4 medium**, and **3 low** severity findings. The most critical issues are an unrestricted raw-command passthrough exposed as a public API and missing path validation on all file-output methods.

---

## Findings

### [HIGH-01] Arbitrary Command Injection via `SendRawCommand`

**Files:** `NanoVNA.cs:655–658`, `NanoVNATestStand.cs:342–348`
**CWE:** CWE-78 (Improper Neutralization of Special Elements)

Both `NanoVNA.SendRawCommand` and `NanoVNATestStand.SendRawCommand` accept a caller-supplied string and write it verbatim to the serial port with only `"\r"` appended:

```csharp
public string SendRawCommand(string command)
{
    SendCommand(command + "\r");
    return FetchData();
}
```

Any caller — including TestStand sequence parameters sourced from test fixtures, configuration files, or UI inputs — can inject arbitrary firmware commands. Depending on NanoVNA firmware, this could:

- Reset or corrupt device calibration
- Trigger undocumented firmware commands
- Cause device firmware crashes or undefined behavior

**Recommendation:** Maintain an explicit allowlist of permitted commands. Reject or sanitize any input that does not match. If arbitrary passthrough is genuinely required, document the trust boundary clearly and restrict callers via interface visibility (e.g., `internal`).

---

### [HIGH-02] Path Traversal in All File-Output Methods

**Files:** `NanoVNA.cs:540–546`, `NanoVNATestStand.cs:277–292`, `NanoVNATestStand.cs:304–319`, `NanoVNATestStand.cs:327–334`, `OutputFormatters.cs:94–128`, `TouchstoneWriter.cs:23–40`
**CWE:** CWE-22 (Improper Limitation of a Pathname to a Restricted Directory)

Every file-write method passes the caller-supplied `filePath` directly to `StreamWriter` or `Bitmap.Save` without validation:

```csharp
// OutputFormatters.cs:96
using (StreamWriter writer = new StreamWriter(filePath))

// NanoVNA.cs:543
bmp.Save(filePath);
```

An attacker who controls this parameter can:

- Write measurement data or captured screenshots to arbitrary filesystem locations (e.g., `../../startup.csv`, `C:\Windows\System32\x.csv`)
- Overwrite existing files
- Write to UNC network paths (`\\attacker\share\file.csv`)

**Recommendation:** Validate that the resolved absolute path (`Path.GetFullPath`) is within an expected output directory before opening the file. Throw `ArgumentException` if the path escapes the allowed root.

---

### [MEDIUM-01] Denial of Service via Infinite Serial Read Loop

**File:** `NanoVNA.cs:115–151`
**CWE:** CWE-400 (Uncontrolled Resource Consumption)

`FetchData()` loops indefinitely without any timeout, waiting for the `ch>` prompt:

```csharp
while (true)
{
    int b = _serial.ReadByte();   // blocks forever
    if (b < 0)
        break;
    // ...
}
```

The `SerialPort.ReadTimeout` is never set (defaults to `SerialPort.InfiniteTimeout`). If the device firmware hangs, sends a corrupted response, or the USB connection is interrupted mid-transmission, the calling thread will block permanently. In a TestStand environment this will deadlock an entire test step and require manual intervention to recover.

**Recommendation:** Set `_serial.ReadTimeout` to a reasonable value (e.g., 5 000 ms) immediately after opening the port. Handle `TimeoutException` in `FetchData()` with a descriptive error and clean state.

---

### [MEDIUM-02] Unsafe Parsing in `FetchGamma` — Missing Bounds Check and Throwing Parse

**File:** `NanoVNA.cs:337–341`
**CWE:** CWE-20 (Improper Input Validation)

`FetchGamma` uses `double.Parse` (throws on failure) and does not check array bounds before indexing:

```csharp
string[] parts = data.Trim().Split(' ');
double real = double.Parse(parts[0], ...);   // FormatException if non-numeric
double imag = double.Parse(parts[1], ...);   // IndexOutOfRangeException if < 2 tokens
```

Any malformed device response — due to interference, a firmware bug, or a man-in-the-middle on the USB bus — causes an unhandled exception that propagates to the caller. By contrast, all other data-fetch methods (`FetchArray`, `Data`) use `double.TryParse` and skip malformed lines defensively.

**Recommendation:** Guard with `parts.Length >= 2` and use `double.TryParse`, or wrap in a `try/catch` and throw a domain-specific exception with context.

---

### [MEDIUM-03] Silent Exception Swallowing Masks Security Events

**Files:** `NanoVNA.cs:80–81`, `NanoVNATestStand.cs:43–50`, `NanoVNATestStand.cs:119–129`
**CWE:** CWE-390 (Detection of Error Condition Without Action)

Multiple bare `catch { }` blocks silently discard all exceptions:

```csharp
// NanoVNA.cs:80-81
try { _serial.DiscardInBuffer(); } catch { }
try { _serial.DiscardOutBuffer(); } catch { }

// NanoVNATestStand.cs:119-129
catch { /* ignore warm-up errors */ }
```

`DetectPort()` and `DetectAllPorts()` also swallow all exceptions and return sentinel values instead. A failed WMI query (e.g., due to insufficient permissions, security policy, or a WMI service outage) is indistinguishable from a "device not found" condition.

**Recommendation:** Log exceptions (even to `Trace`/`Debug`) before swallowing them. Catch specific exception types (e.g., `InvalidOperationException`, `UnauthorizedAccessException`) rather than all exceptions. For security-relevant failures such as access denied, propagate rather than mask.

---

### [MEDIUM-04] Unvalidated Device Response Data Used in Array Operations

**Files:** `NanoVNA.cs:247–250`, `NanoVNA.cs:277–280`
**CWE:** CWE-20 (Improper Input Validation)

`FetchBuffer` and `FetchRawWave` parse hex tokens from device output using `Convert.ToInt32(hex, 16)` and cast to `short` without validation:

```csharp
values.Add((short)Convert.ToInt32(hex, 16));
```

A compromised or malfunctioning device that sends out-of-range hex values (e.g., `FFFF0001`) will cause `Convert.ToInt32` to throw `OverflowException`. A value that fits in `int` but not `short` is silently truncated by the unchecked cast, producing corrupted measurement data with no error indication. In a safety-related test environment this could cause incorrect pass/fail decisions.

**Recommendation:** Use `short.TryParse` or validate bounds before the cast. Propagate or log `FormatException`/`OverflowException` from the parsing step.

---

### [LOW-01] Information Disclosure via Exception Messages

**File:** `NanoVNATestStand.cs:148–149`, `NanoVNATestStand.cs:110–111`
**CWE:** CWE-209 (Generation of Error Message Containing Sensitive Information)

`ConnectWithStatus` and `LastError` return or store raw exception messages:

```csharp
return ex.GetType().Name + ": " + ex.Message;
LastError = "DEVICE_NOT_FOUND: " + lastEx.Message;
```

Exception messages from `SerialPort`, `ManagementObjectSearcher`, and `System.IO` can contain:

- Absolute file system paths
- Windows user account names
- COM port security configuration details
- System driver version strings

These details are typically unnecessary for the caller and could assist an attacker in fingerprinting the host environment.

**Recommendation:** Log the full exception internally. Return a sanitized message (or a fixed error code) to external callers.

---

### [LOW-02] No Validation on Numeric Sweep Parameters

**Files:** `NanoVNATestStand.cs:186–232`, `NanoVNA.cs:161–166`
**CWE:** CWE-20 (Improper Input Validation)

`MeasureAndEvaluate`, `MeasureToCsv`, and `MeasureToTouchstone` accept sweep parameters (`startHz`, `stopHz`, `points`, `z0`) without any range checking. Problematic inputs include:

- `points <= 0`: `Linspace` returns an empty array; downstream array indexing (`Frequencies[0]`) throws `IndexOutOfRangeException`
- `startHz >= stopHz`: Produces a meaningless or empty frequency array
- `z0 = 0`: `ComputeImpedance` divides by zero (producing `NaN`/`Infinity`) without any indication
- Extremely large `points` values: Could cause out-of-memory conditions

**Recommendation:** Add guard clauses at public entry points validating parameter ranges before use (e.g., `points >= 1`, `startHz < stopHz`, `z0 > 0`).

---

### [LOW-03] Double Disposal of `NanoVNA` in `Disconnect`

**File:** `NanoVNATestStand.cs:159–165`
**CWE:** CWE-675 (Multiple Operations on Resource in Wrong Phase of Lifetime)

`Disconnect()` calls both `_nv.Close()` and `_nv.Dispose()` sequentially:

```csharp
_nv.Close();
_nv.Dispose();
```

`NanoVNA.Dispose()` internally calls `Close()` again, resulting in a double-close of the serial port. The existing null-checks in `Close()` prevent a crash, but the pattern is fragile: if `Close()` has side effects (e.g., the 1-second `Thread.Sleep`) they will execute twice, introducing unexpected latency. It also violates the expected dispose-once contract.

**Recommendation:** Remove the explicit `_nv.Close()` call in `Disconnect()` and rely solely on `_nv.Dispose()`, or call only `Close()` and skip the separate `Dispose()` call (since `Dispose` calls `Close` anyway).

---

## Summary Table

| ID       | Severity | File(s)                                    | Description                                              |
|----------|----------|--------------------------------------------|----------------------------------------------------------|
| HIGH-01  | High     | NanoVNA.cs, NanoVNATestStand.cs            | Arbitrary command injection via `SendRawCommand`         |
| HIGH-02  | High     | Multiple file-output methods               | Path traversal — no output path validation               |
| MED-01   | Medium   | NanoVNA.cs                                 | Infinite serial read loop — no timeout set               |
| MED-02   | Medium   | NanoVNA.cs                                 | `FetchGamma` uses throwing `Parse` without bounds check  |
| MED-03   | Medium   | NanoVNA.cs, NanoVNATestStand.cs            | Silent broad exception swallowing masks errors           |
| MED-04   | Medium   | NanoVNA.cs                                 | Unvalidated device hex data — silent integer truncation  |
| LOW-01   | Low      | NanoVNATestStand.cs                        | Exception messages expose system information             |
| LOW-02   | Low      | NanoVNATestStand.cs, NanoVNA.cs            | No validation on numeric sweep parameters                |
| LOW-03   | Low      | NanoVNATestStand.cs                        | Double disposal of `NanoVNA` in `Disconnect`             |

---

## Prioritized Remediation Plan

1. **(HIGH-02) Add path validation to all file-output methods.** This is the most straightforward fix and eliminates a broad attack surface.
2. **(HIGH-01) Restrict or remove `SendRawCommand`.** Introduce a command allowlist or change the visibility of the method.
3. **(MED-01) Set `SerialPort.ReadTimeout`.** Set immediately after `_serial.Open()` in `NanoVNA.Open()`.
4. **(MED-02) Fix `FetchGamma` parsing.** Add bounds check and switch to `TryParse`.
5. **(MED-03) Replace bare `catch {}` blocks.** Log exceptions; catch specific types.
6. **(MED-04) Validate parsed device data.** Add range checks before the `short` cast.
7. **(LOW-01) Sanitize error return values.** Return error codes rather than raw exception messages.
8. **(LOW-02) Add parameter guard clauses.** Validate sweep parameters at public API entry points.
9. **(LOW-03) Fix double disposal.** Remove the redundant `Close()` call in `Disconnect()`.
