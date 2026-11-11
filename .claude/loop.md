# 자동 개선 루프 규칙

한 번 실행될 때마다 개선 항목 "딱 1개"만 처리한다.

## 순서
1. git status가 깨끗한지 확인. 커밋 안 된 변경이 있으면 아무것도 하지 말고 docs/auto-log.md에 "작업 중인 변경 있음 - 건너뜀"만 남기고 종료.
2. main에서 새 브랜치 생성: auto/날짜-주제 (예: auto/20261112-queue-file-move)
3. docs/backlog.md에서 가장 작고 안전한 항목 1개 선택. backlog가 비었으면 code-reviewer로 코드베이스를 훑어 개선 후보 3개를 docs/proposals.md에 "제안만" 적고 종료.
4. 구현 → dotnet build → test-writer로 테스트 → dotnet test → code-reviewer로 리뷰
5. 🔴가 나오면 고치고 다시 검증. 3번 시도해도 빌드·테스트가 실패하면 브랜치 변경을 버리고 실패 이유만 기록.
6. 통과하면 해당 브랜치에 커밋하고, backlog.md에서 그 항목에 "완료(브랜치명)" 표시.
7. docs/auto-log.md에 기록: 시각, 브랜치, 처리 항목, 변경 파일, 테스트 수, 리뷰 결과, "내가 배울 점" 3줄
8. main으로 돌아가서 종료.

## 절대 금지
- main에 직접 커밋, merge, push 금지 (합치는 건 사람이 한다)
- CLAUDE.md 절대 규칙 수정 금지
- backlog에 없는 새 기능 구현 금지 (proposals.md에 제안만)
- sc.exe, 관리자 권한 명령, 레지스트리 쓰기 금지
- 파일 삭제는 이번 작업에서 새로 만든 파일만
