#!/bin/sh
# Double-click entry for Finder: Terminal runs this file, which hands over to install.sh next to it.
# A downloaded copy is blocked once by Gatekeeper: System Settings -> Privacy & Security -> Open Anyway.
exec sh "$(dirname "$0")/install.sh"
