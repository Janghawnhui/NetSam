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

## 5. 실시간 연결(Phase 7) 설정

서비스가 서버 Hub(`/hubs/agent`)에 붙으려면 등록(enrollment) 비밀값과 서버 쪽 설정이 먼저 준비돼 있어야 합니다.

### 5-1. 서버 쪽 — 등록 비밀값을 user-secrets로 설정

```powershell
dotnet user-secrets set "Realtime:EnrollmentSecret" "<충분히 긴 임의의 문자열>" --project src/NetSamLite.Server
```

`appsettings.json`에는 절대 넣지 않습니다(절대 규칙 4). 운영 환경(게시본)에서는 환경변수 `Realtime__EnrollmentSecret`로 같은 값을 설정해도 됩니다.

### 5-2. 에이전트 쪽 — 같은 비밀값을 환경변수로 설정

서비스가 SYSTEM 계정으로 돌기 때문에, 사용자 세션의 환경변수가 아니라 **시스템 환경변수**로 등록해야 서비스에서 보입니다.

```powershell
setx NETSAMLITE_ENROLLMENT_SECRET "<서버와 동일한 값>" /M
```

`/M`(머신 범위)을 빼면 현재 로그온 사용자 세션에만 적용돼 SYSTEM 계정으로 돌아가는 서비스에서는 보이지 않습니다. 설정 후 서비스를 재시작해야 반영됩니다(`sc stop` → `sc start`).

최초 연결 시 에이전트가 이 비밀값으로 `POST /api/v1/devices/{deviceId}/enroll`을 호출해 장치 토큰을 발급받고, `AgentOptions.TokenFilePath`(기본 `%ProgramData%\NetSamLite\device-token.dat`)에 저장합니다. 이후에는 비밀값 없이 저장된 토큰만 씁니다.

### 5-3. 로컬호스트/신뢰 네트워크 전용 — HTTPS 미강제

Server의 기본 프로파일은 `http://localhost:5136`이고, Hub 인증에 쓰는 베어러 토큰이 암호화되지 않은 HTTP로 오갑니다(`docs/backlog.md`의 "HTTPS가 강제되지 않아..." 항목 참고). 따라서 이 구성은 **같은 신뢰 네트워크(가정/사내망) 안에서만** 쓰는 것을 전제로 합니다. 인터넷에 노출된 네트워크에서는 쓰지 마세요.

### 5-4. "지금 수집"은 Web이 Server와 같은 PC에서 떠 있어야 동작함

대시보드의 "지금 수집" 버튼이 호출하는 `POST /api/v1/devices/{deviceId}/collect-now`는 루프백(loopback) 호출만 허용합니다(확정 결정 3 — Phase 9 진입 게이트가 요구하는 운영자 인증이 아직 없어서 당장은 이 방식으로 막아 둔 것입니다). 즉 **`NetSamLite.Web`을 `NetSamLite.Server`와 다른 PC에서 띄우면 "지금 수집" 호출이 항상 403으로 거부됩니다.** 두 프로세스를 같은 PC에서 띄우세요.
