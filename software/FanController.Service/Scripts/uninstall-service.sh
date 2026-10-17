#!/usr/bin/env bash
set -euo pipefail

# ---- Configuration ----------------------------------------------------------

SERVICE_NAME="fancontrollerservice"
INSTALL_DIR="/opt/fancontrollerservice"
SYSTEMD_UNIT="/etc/systemd/system/${SERVICE_NAME}.service"

# ---- Colour helpers ---------------------------------------------------------

RED='\033[0;31m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; YELLOW='\033[1;33m'; NC='\033[0m'
step()    { echo -e "${CYAN}[*] $*${NC}";            }
ok()      { echo -e "${GREEN}[+] $*${NC}";           }
skipped() { echo -e "${YELLOW}[-] $*${NC}";          }
err()     { echo -e "${RED}[!] $*${NC}" >&2; exit 1; }

# ---- Privilege check --------------------------------------------------------

[[ $EUID -eq 0 ]] || err "Run this script as root (sudo $0)."

# ---- Stop and disable service -----------------------------------------------

step "Looking for service '${SERVICE_NAME}'..."

if systemctl list-unit-files --quiet "${SERVICE_NAME}.service" &>/dev/null; then

    if systemctl is-active --quiet "${SERVICE_NAME}.service"; then
        step "Stopping service..."
        systemctl stop "${SERVICE_NAME}.service"
        ok "Stopped."
    else
        skipped "Service was not running."
    fi

    step "Disabling service..."
    systemctl disable "${SERVICE_NAME}.service" 2>/dev/null || true
    ok "Disabled."

else
    skipped "Service '${SERVICE_NAME}' not found – skipping stop/disable."
fi

# ---- Remove unit file -------------------------------------------------------

if [[ -f "$SYSTEMD_UNIT" ]]; then
    step "Removing unit file: $SYSTEMD_UNIT"
    rm -f "$SYSTEMD_UNIT"
    ok "Unit file removed."
else
    skipped "Unit file not found – skipping."
fi

step "Reloading systemd daemon..."
systemctl daemon-reload
systemctl reset-failed 2>/dev/null || true

# ---- Remove installed files -------------------------------------------------

if [[ -d "$INSTALL_DIR" ]]; then
    step "Removing install directory: $INSTALL_DIR"
    rm -rf "$INSTALL_DIR"
    ok "Files removed."
else
    skipped "Directory '$INSTALL_DIR' not found – skipping."
fi

ok "Uninstall complete."
