# Security Policy

## Known limitations

The Fan Controller service accepts **unauthenticated, unencrypted** gRPC connections
on TCP port 51515 on **all network interfaces**, and runs with administrator / root
privileges. Anyone who can reach this port can read the sensor data and change the
controller configuration, including a factory reset.

- Run the service only on trusted networks.
- Block the port in your firewall if you don't need remote access.
- On Windows, remote access requires an inbound firewall rule for the port;
  on Linux, the port is usually reachable without one.

The controller's USB interface has no authentication either: a program on the
computer that can open the controller's USB HID device can read and change its
configuration.

This is the intended behavior of the current versions and not a vulnerability in itself.

## Supported versions

Security fixes are provided for the latest firmware release and the latest software
release only.

## Reporting a vulnerability

Please **don't** report vulnerabilities in public issues. Use GitHub's private
vulnerability reporting instead: *Security* tab → *Report a vulnerability*.

Please include the affected component and version, a description of the problem and,
if possible, steps to reproduce it. You will receive an answer as soon as possible.
