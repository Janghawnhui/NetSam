# NetSamLite.Agent — Windows 서비스 등록/삭제

이 문서는 명령어만 정리해둔 것이고, **직접 실행하지 않았습니다.** 서비스 등록·삭제는 관리자 권한이 필요하고 시스템에 영향을 주는 작업이라, CLAUDE.md 규칙대로 실행 전 항상 확인을 받습니다.

## 0. 게시(publish)

서비스는 `dotnet run`이 아니라 게시된 실행 파일로 등록합니다.

```powershell
dotnet publish src\NetSamLite.Agent -c Release -o C:\Apps\NetSamLite\Agent
```

`appsettings.json`(수집 주기, jitter, 재시도, 큐/로그 경로)도 같은 폴더에 같이 게시됩니다. 서비스 등록 전에 원하는 값으로 수정해두세요.

## 1. 서비스 등록

관리자 권한 PowerShell/명령 프롬프트에서:

```powershell
sc create NetSamLiteAgent binPath= "C:\Apps\NetSamLite\Agent\NetSamLite.Agent.exe" start= auto obj= "NT AUTHORITY\SYSTEM"
sc description NetSamLiteAgent "NetSamLite 설치 SW/HW 수집 에이전트"
```

- `binPath=`, `start=`, `obj=` 뒤에 **공백이 꼭 있어야** 합니다 (`sc.exe` 특유의 문법).
- `obj= "NT AUTHORITY\SYSTEM"`은 기본값이라 생략해도 되지만, "SYSTEM 계정으로 돈다"는 걸 명시적으로 남기기 위해 적었습니다. SYSTEM으로 돌기 때문에 사용자별 설치 SW는 `HKCU` 대신 `HKEY_USERS` 열거로 수집합니다(현재 로그인해 있는 사용자만 보임 — Phase 6 설계 참고).
- `start= auto`는 부팅 시 자동 시작.

## 2. 시작 / 중지 / 상태 확인

```powershell
sc start NetSamLiteAgent
sc stop NetSamLiteAgent
sc query NetSamLiteAgent
```

## 3. 서비스 삭제

```powershell
sc stop NetSamLiteAgent
sc delete NetSamLiteAgent
```

**삭제해도 다음은 자동으로 안 지워집니다** — 필요하면 수동으로 정리하세요.
- 로그: `AgentOptions.LogDirectory` 기본값 `%ProgramData%\NetSamLite\logs\`
- 전송 실패 큐: `AgentOptions.QueueDirectory` 기본값 `%ProgramData%\NetSamLite\queue\` (삭제 전에 큐에 남은 리포트가 있는지 먼저 확인하는 걸 권장 — 미전송 데이터가 있을 수 있음)
- 게시 파일: 1번에서 publish한 폴더

## 4. 수동 테스트(서비스 등록 없이)

서비스로 등록하기 전에 콘솔에서 직접 돌려보고 싶으면:

```powershell
C:\Apps\NetSamLite\Agent\NetSamLite.Agent.exe
```

인자 없이 실행하면 Generic Host가 뜨고(Windows SCM 밑이 아니므로 그냥 콘솔 앱으로 동작), 설정된 주기로 수집·전송을 반복합니다. `Ctrl+C`로 종료.

`--once --out <경로>` 인자를 주면 Phase 1 때와 동일하게 1회 수집 후 즉시 종료합니다(서비스 모드와 무관).
