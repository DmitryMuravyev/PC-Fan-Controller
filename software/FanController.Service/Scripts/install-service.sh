#!/usr/bin/env bash
set -euo pipefail

# ---- Configuration ----------------------------------------------------------

SERVICE_NAME="fancontrollerservice"
DISPLAY_NAME="Fan Controller Service"
DESCRIPTION="Manages fan speed control based on temperature sensors."
BINARY_NAME="FanController.Service"
INSTALL_DIR="/opt/fancontrollerservice"
SERVICE_USER="root"           # run as root (system); change to a dedicated
SERVICE_GROUP="root"          # user for better security if preferred
SYSTEMD_UNIT="/etc/systemd/system/${SERVICE_NAME}.service"

# ---- Colour helpers ---------------------------------------------------------

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
step() { echo -e "${CYAN}[*] $*${NC}";  }
ok()   { echo -e "${GREEN}[+] $*${NC}"; }
err()  { echo -e "${RED}[!] $*${NC}" >&2; exit 1; }

# ---- Privilege check --------------------------------------------------------

[[ $EUID -eq 0 ]] || err "Run this script as root (sudo $0)."

# ---- Locate binary ----------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BINARY_PATH="$SCRIPT_DIR/$BINARY_NAME"

[[ -f "$BINARY_PATH" ]] || \
    err "Binary not found: $BINARY_PATH\nPlace '$BINARY_NAME' next to this script."

# ---- Stop existing service (idempotent) -------------------------------------

step "Checking for existing service..."
if systemctl list-unit-files --quiet "${SERVICE_NAME}.service" &>/dev/null && \
   systemctl is-active --quiet "${SERVICE_NAME}.service"; then
    step "Stopping existing service..."
    systemctl stop "${SERVICE_NAME}.service"
    ok "Stopped."
fi

if systemctl list-unit-files --quiet "${SERVICE_NAME}.service" &>/dev/null; then
    step "Disabling existing service..."
    systemctl disable "${SERVICE_NAME}.service" 2>/dev/null || true
fi

# ---- Copy files -------------------------------------------------------------

step "Creating install directory: $INSTALL_DIR"
mkdir -p "$INSTALL_DIR"

step "Copying files..."
# Copy everything that lives next to this script (binary, configs, DLLs, etc.)
find "$SCRIPT_DIR" -maxdepth 1 -type f \
    ! -name "$(basename "${BASH_SOURCE[0]}")" \
    -exec cp -f {} "$INSTALL_DIR/" \;

INSTALLED_BINARY="$INSTALL_DIR/$BINARY_NAME"
chmod +x "$INSTALLED_BINARY"

# ---- Lock down permissions --------------------------------------------------

step "Setting directory permissions..."
chown -R "${SERVICE_USER}:${SERVICE_GROUP}" "$INSTALL_DIR"
chmod 750 "$INSTALL_DIR"
find "$INSTALL_DIR" -type f -exec chmod 640 {} \;
chmod 750 "$INSTALLED_BINARY"

# ---- Write systemd unit file ------------------------------------------------

step "Writing systemd unit: $SYSTEMD_UNIT"
cat > "$SYSTEMD_UNIT" <<EOF
[Unit]
Description=${DISPLAY_NAME} - ${DESCRIPTION}
After=network.target
# Uncomment if you need the network to be fully up before starting:
# After=network-online.target
# Wants=network-online.target

[Service]
Type=notify
# --- Identity ---
User=${SERVICE_USER}
Group=${SERVICE_GROUP}
# --- Binary ---
ExecStart=${INSTALLED_BINARY}
WorkingDirectory=${INSTALL_DIR}
# --- Restart policy ---
Restart=on-failure
RestartSec=5s
StartLimitIntervalSec=120
StartLimitBurst=5
# --- Logging ---
StandardOutput=journal
StandardError=journal
SyslogIdentifier=${SERVICE_NAME}
# --- Hardening (optional but recommended) ---
ProtectSystem=full
PrivateTmp=true
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
EOF

chmod 644 "$SYSTEMD_UNIT"

# ---- Enable and start -------------------------------------------------------

step "Reloading systemd daemon..."
systemctl daemon-reload

step "Enabling service (auto-start on boot)..."
systemctl enable "${SERVICE_NAME}.service"

step "Starting service..."
systemctl start "${SERVICE_NAME}.service"

# Give it a moment and verify
sleep 2
if systemctl is-active --quiet "${SERVICE_NAME}.service"; then
    ok "Service '${SERVICE_NAME}' is running."
else
    err "Service failed to start. Check logs with:\n  journalctl -u ${SERVICE_NAME}.service -n 50"
fi

ok "Install directory : $INSTALL_DIR"
ok "Unit file         : $SYSTEMD_UNIT"
ok ""
ok "Useful commands:"
ok "  Status  : systemctl status ${SERVICE_NAME}"
ok "  Logs    : journalctl -u ${SERVICE_NAME} -f"
ok "  Stop    : systemctl stop ${SERVICE_NAME}"
ok "Done."
