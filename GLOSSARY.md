# FTMS Viewer

A MAUI viewer for Bluetooth LE fitness machines implementing the Fitness Machine Service (FTMS). It connects to a machine, observes its live data, features, and state, and lets the user drive it through the Control Point.

## Language

**Fitness Machine**:
A Bluetooth LE device implementing the Fitness Machine Service — treadmills, indoor bikes, rowers, cross-trainers, stair climbers, and step climbers.
_Avoid_: Device, peripheral, trainer

**Machine Type**:
The category of a fitness machine, which determines which data characteristics and which target settings are meaningful for it.

**Control Point**:
The BLE characteristic through which the app writes control requests to the machine. The machine answers each request with a control response.

**Control Request**:
An op code plus optional parameters, written to the Control Point.

**Control Response**:
The machine's answer to a control request, carrying a result code — success or a specific rejection reason.
_Avoid_: Ack, reply

**Control Permission**:
The machine's grant to the app to send control requests. Machines can revoke it (reported as control-permission-lost); requests sent without it are rejected with a "control permission not granted" result code.

**Control Operation**:
A control request that carries no value — request control, reset, start/resume, stop, pause, spin-down start/ignore.
_Avoid_: Command, action

**Target Setting**:
A control request that sets a value the machine should apply or aim for — target speed, incline, resistance level, power, heart rate, cadence, or a targeted distance, time, energy, steps, or strides.
_Avoid_: Set, control

**Target Setting Support**:
A machine capability flag indicating whether it accepts a given target setting.

**Supported Range**:
The minimum, maximum, and minimum increment a machine advertises for a target setting, expressed in human units (km/h, %, W, bpm, rpm).

**Current Target**:
The value of a target setting the machine is currently applying, as reported through machine-state notifications.

**Machine State**:
Notifications from the machine about changed parameters — current targets, spin-down state, and control-permission changes.
_Avoid_: Status, notification

**Training State**:
The machine's lifecycle state — not ready, idle, running, paused, and similar.

**Spin-Down Control**:
A two-phase calibration procedure for wheel-based trainers; the machine answers with the target speed bounds it measured.