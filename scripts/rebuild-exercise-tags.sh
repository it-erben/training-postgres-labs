#!/usr/bin/env bash
# Setzt die Kette der Übungs-Tags auf den aktuellen Stand von main.
#
# Jeder Tag uebung-NN-start und uebung-NN-loesung zeigt auf einen Commit,
# der nur Rental/ und Rental.Tests/ ändert. Das Skript übernimmt diese
# Commits der Reihe nach per cherry-pick auf main und verschiebt die Tags.
# Damit enthält jeder Tag sql/ und dotnet/ im Stand von main.
#
# Nutzung: scripts/rebuild-exercise-tags.sh [basis]   (Standard: main)
# Danach:  git push --force origin 'refs/tags/uebung-*'
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
basis=${1:-main}

tags="uebung-00-start
uebung-01-start uebung-01-loesung
uebung-02-start uebung-02-loesung
uebung-03-start uebung-03-loesung
uebung-04-start uebung-04-loesung
uebung-05-start uebung-05-loesung
uebung-06-start uebung-06-loesung"

# Die alten Commits vor dem Umbau festhalten, weil das Skript die Tags
# unterwegs verschiebt.
alt=""
for t in $tags; do
  alt="$alt $t=$(git rev-parse "$t^{commit}")"
done

arbeit=$(mktemp -d)
trap 'git worktree remove --force "$arbeit" >/dev/null 2>&1 || true' EXIT
git worktree add --quiet --detach "$arbeit" "$basis"

for paar in $alt; do
  t=${paar%%=*}
  c=${paar#*=}
  git -C "$arbeit" cherry-pick --allow-empty "$c" >/dev/null
  git tag -f "$t" "$(git -C "$arbeit" rev-parse HEAD)" >/dev/null
  echo "$t -> $(git rev-parse --short "$t") $(git log -1 --format=%s "$t")"
done
