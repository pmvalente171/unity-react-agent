#!/bin/sh
# Probe a model deployment in a throwaway container, using the env vars in .env.
# Usage: ./probe_model.sh <card name>, e.g. ./probe_model.sh gemma
# Run this yourself: it reads .env, which Claude Code is not allowed to read.
set -e
cd "$(dirname "$0")"
[ -n "$1" ] || { echo "usage: $0 <card name>"; exit 1; }
SRC="$(pwd -W 2>/dev/null || pwd)"
MSYS_NO_PATHCONV=1 docker run --rm --env-file .env -v "$SRC:/src:ro" -w /work python:3.12-slim sh -c \
  "tar -C /src --exclude=./.env -cf - . | tar -C /work -xf - && pip install -q --root-user-action=ignore -r requirements.txt >/dev/null 2>&1 && python -I tools/probe_model.py $1"
