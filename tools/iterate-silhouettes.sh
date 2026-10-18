#!/usr/bin/env bash
# Author -> apply -> build -> project -> measure, for the given hull IDs.
# tools/iterate-silhouettes.sh OUT_SUBDIR ID [ID ...]
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; cd "$ROOT"
WIN_ROOT="$(cygpath -m "$ROOT")"
B="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"
O=docs/validation/silhouette_match_20261018; RUN=$1; shift
KEYS="$*"; CSV=$(echo $KEYS | tr ' ' ',')
python tools/author-reference-layouts.py $KEYS > /dev/null
python tools/apply-model-layouts.py $KEYS > /dev/null
"$B" --background --factory-startup --python-exit-code 1 --python tools/build-reference-models.py -- --root "$WIN_ROOT" --render --ship $CSV > $O/build-$RUN.log 2>&1 || { grep -E "Error|line [0-9]" $O/build-$RUN.log | tail -5; exit 1; }
"$B" --background --factory-startup --python tools/silhouette-export.py -- --root "$WIN_ROOT" --out $O/sil-$RUN --ship $CSV > /dev/null 2>&1
python tools/silhouette-match.py $O/sil-$RUN $O/$RUN $KEYS | grep -v "^Mean"
