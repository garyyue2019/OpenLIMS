#!/usr/bin/env bash
set -euo pipefail

profile=""
module=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --profile) profile="$2"; shift 2 ;;
    --module) module="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

case "$profile" in task|architecture|contracts|all) ;; *) echo "--profile task|architecture|contracts|all is required" >&2; exit 2 ;; esac
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

require_command() { command -v "$1" >/dev/null 2>&1 || { echo "Required command '$1' is not available. Install the locked toolchain before running verification." >&2; exit 1; }; }
gate() { echo "==> $1"; shift; "$@"; }
dotnet_test() { require_command dotnet; gate "dotnet test ($1)" dotnet test OpenLIMS.slnx -c Release --no-build --filter "$1"; }

case "$profile" in
  task)
    case "$module" in
      platform) test_filter='FullyQualifiedName~Platform' ;;
      module-onboarding) test_filter='Profile=module-onboarding' ;;
      receiving) test_filter='Profile=receiving' ;;
      labeling) test_filter='Profile=labeling' ;;
      scope) test_filter='Profile=scope' ;;
      quantity) test_filter='Profile=quantity' ;;
      *) echo "The task profile requires --module platform, --module module-onboarding, --module receiving, --module labeling, --module scope, or --module quantity." >&2; exit 2 ;;
    esac
    require_command dotnet
    gate "dotnet restore (locked)" dotnet restore OpenLIMS.slnx --locked-mode
    gate "dotnet build" dotnet build OpenLIMS.slnx -c Release --no-restore -warnaserror
    dotnet_test "$test_filter"
    ;;
  architecture) dotnet_test 'FullyQualifiedName~Architecture' ;;
  contracts) dotnet_test 'FullyQualifiedName~Contract' ;;
  all)
    "$0" --profile task --module platform
    "$0" --profile architecture
    "$0" --profile contracts
    require_command corepack
    gate "pnpm install (frozen)" corepack pnpm@10.34.5 install --frozen-lockfile
    gate "pnpm lint" corepack pnpm@10.34.5 --dir apps/web lint
    gate "pnpm typecheck" corepack pnpm@10.34.5 --dir apps/web typecheck
    gate "pnpm unit tests" corepack pnpm@10.34.5 --dir apps/web test:unit
    gate "pnpm build" corepack pnpm@10.34.5 --dir apps/web build
    require_command docker
    gate "docker compose config" docker compose --env-file deploy/compose/.env.example -f deploy/compose/compose.yaml config --quiet
    images="$(docker compose --env-file deploy/compose/.env.example -f deploy/compose/compose.yaml config --images)"
    [[ -n "$images" ]] || { echo "Compose configuration did not yield any images." >&2; exit 1; }
    while IFS= read -r image; do
      [[ "$image" =~ @sha256:[a-f0-9]{64}$ ]] || { echo "Compose image is not pinned to a SHA-256 digest: $image" >&2; exit 1; }
    done <<< "$images"
    require_command python
    gate "specgen check" python -m tools.specgen check
    ;;
esac
