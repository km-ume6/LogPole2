# Copilot Instructions

## プロジェクト ガイドライン
- The user prefers bundled proposals and changes in a single pass rather than step-by-step incremental suggestions.
- インターバル設定はミリ秒ではなく秒単位で統一する。インターバルは内部実装も秒単位で統一し、設定可能な上限は3600秒程度にする。
- Avoid certificate-based signing workflows when possible.