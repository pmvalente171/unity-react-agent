#!/bin/sh
# Run the test suite in a throwaway Python container (Python isn't installed on the dev machine).
# Usage: ./run_tests.sh [extra pytest args], e.g. ./run_tests.sh -k agent -x
set -e
cd "$(dirname "$0")"
SRC="$(pwd -W 2>/dev/null || pwd)"  # Windows path under Git Bash, POSIX path elsewhere
MSYS_NO_PATHCONV=1 docker run --rm -v "$SRC:/src:ro" -w /work python:3.12-slim sh -c \
  "tar -C /src --exclude=./.env -cf - . | tar -C /work -xf - && pip install -q --root-user-action=ignore -r requirements-dev.txt >/dev/null 2>&1 && python -m pytest -q -p no:cacheprovider tests $*"
