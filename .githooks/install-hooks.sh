#!/bin/sh
set -e
git config --local core.hooksPath .githooks
echo "Pre-commit hook enabled for this repository."
