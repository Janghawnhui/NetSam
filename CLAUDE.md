# NetSAM-Lite — SW 자산관리(SAM) 학습 프로젝트

PC에 설치된 SW/HW 정보를 에이전트로 수집하고, 서버에서 정규화·라이선스 비교·리포트를 하는 미니 SAM 솔루션.
목적은 **상용 SAM 솔루션의 동작 원리를 직접 구현하며 이해하는 것**이다. 완성도보다 이해가 우선.

---

## 🚫 절대 규칙 (위반 금지 — 최우선 적용)

1. **회사 자산 사용 금지**: 재직 중인 회사의 소스코드, 내부 문서, SW DB, 고객 데이터를 참조·복사·추측 재현하지 않는다. 공개 문서(Microsoft Docs 등)와 직접 구현만 사용한다.
2. **능동 조치는 서버 서명 정책에 한정**: 레지스트리·WMI 조회 전용 원칙은 유지하며 **레지스트리 쓰기는 영구 금지**(Phase 10 이후에도 변경 불가). 프로세스 종료·방화벽 규칙 변경은 서버가 발급한 **서명된 정책(PolicyDocument + 서명)**을 로컬에서 검증했을 때만 실행한다. 서명 누락·불일치·버전 다운그레이드(replay)는 즉시 거부하고 사유를 감사 로그에 남긴다.
   - **기본 모드 = 감사(Audit)**: 실제 차단 대신 "차단했다면"을 로그·대시보드에만 기록한다. Enforce 전환은 Phase별로 명시적 플래그로만 한다.
   - **보호 프로세스 목록**(OS 필수 프로세스, 에이전트/서버 자기 자신, 사용자 지정 예외)은 서버가 정책 생성 시점에 차단 대상에서 강제 제외한다 — 에이전트 쪽 필터가 아니라 서버 쪽 1차 방어. 실제 목록은 Phase 9/10 기획 시점에 확정한다.
   - **긴급 해제(Kill switch)**: 네트워크가 끊겨도 로컬에서 동작해야 한다 (서비스 중지, 또는 로컬 파일/레지스트리 플래그로 즉시 Audit 모드 강제 복귀).
   - **모든 조치는 감사 로그**: 정책 수신·서명 검증 결과·실행/거부 사유·대상(PID/프로세스명/방화벽 규칙)·타임스탬프를 서버 전송 + 로컬 파일 이중 기록한다(네트워크 단절 대비).
   - **Phase 9 진입 게이트**: 대시보드/운영자 인증이 구현되어 있지 않으면 Phase 9(프로세스 감지·차단)를 시작하지 않는다. Phase 7의 `collect-now`는 조회성 명령이라 localhost 전용 제한으로 충분하지만, Phase 9부터는 "인증 미비 시 임의 명령 주입"이 실제 차단 기능에 직결되므로 운영자 인증이 먼저 갖춰져야 한다.
3. **개인정보 수집 금지**: 아래 「수집 항목」 표에 있는 값만 수집한다. 사용자 파일, 브라우저 기록, 계정 비밀번호, 화면은 절대 수집하지 않는다.
4. **비밀값 커밋 금지**: 연결 문자열, API 키, 인증서는 `dotnet user-secrets` 또는 환경변수로만 다룬다. `appsettings.json`에 넣지 않는다.
5. **`Win32_Product` WMI 클래스 사용 금지**: 조회 시 MSI 무결성 검사·재구성이 일어나 느리고 부작용이 있다. 설치 SW는 레지스트리 Uninstall 키로 수집한다.
6. **현재 Phase 밖 기능 구현 금지**: 아래 「진행 단계」의 현재 Phase 범위만 작업한다. 앞 단계 기능이 필요하면 먼저 묻는다.
7. **계획 먼저, 코드는 나중**: 파일 3개 이상 수정하거나 새 프로젝트를 추가할 때는 수정할 파일 목록과 이유를 먼저 제시하고 승인받는다.
8. **빌드·테스트 통과 전 완료 보고 금지**: `dotnet build`와 `dotnet test`가 통과해야 "완료"라고 말한다.
9. **설명하며 구현**: 나는 학습 중이다. 새로운 API·패턴을 쓰면 왜 그걸 골랐는지 2~3줄로 설명한다.

---

## 🎯 진행 단계 (현재: Phase 8)

| Phase | 목표 | 완료 기준 |
|---|---|---|
| 1 | 수집 에이전트 (콘솔 실행) | `--once` 실행 시 설치 SW·HW 정보를 JSON 파일로 저장 |
| 2 | 서버 API + DB | 에이전트가 POST → DB 저장, 변경 이력(설치/삭제) 기록 |
| 3 | SW 정규화 | 원본 이름·버전을 `SoftwareProduct`로 묶는 규칙 엔진 + 테스트 |
| 4 | 라이선스 매칭 | 보유 수량 vs 설치 수 비교, 초과/미사용 판정 |
| 5 | 대시보드·정책 | Blazor 화면, 금지 SW 알림, CSV 리포트 |
| 6 | 서비스화 | Windows 서비스 등록, 주기 수집, 재시도·로그 |
| 7 | SignalR 실시간 채널 | 에이전트↔서버 Hub 연결, 인증(mTLS/토큰), 끊김 시 지수 백오프 재연결, 대시보드에 연결 상태 표시 |
| **8** | 정책 배포·버전·ACK·로컬 캐시 | 서버 push → 서명 검증 → 버전 비교 → 로컬 캐시 저장 → ACK 전송 → 대시보드에 배포 상태(대기/완료/거부) 표시 |
| 9 | 프로세스 감지·차단 (감사 모드) | 프로세스 생성 이벤트 감지(WMI/ETW) → 금지 목록 매칭 → **감사 로그만** 기록, 실제 종료 없음 |
| 10 | 방화벽 차단 (Enforce) | 서명+Enforce 모드일 때만 실제 프로세스 종료/방화벽 규칙 적용, 보호 목록 체크 선행, 긴급 해제 동작 확인 |

Phase가 끝나면 이 표의 "현재" 표시를 갱신한다.

### Phase 7~10 위험 요소·테스트 방법

| Phase | 주요 위험 | 테스트 방법 |
|---|---|---|
| 7 | SYSTEM 권한 프로세스가 외부 제어 채널을 여는 것 자체가 공격 표면 확대. 인증 미비 시 임의 명령 주입 | Windows Sandbox 내 에이전트 → 호스트의 서버로 연결 확인. 서버 포트를 방화벽으로 막아 재연결/백오프 동작 확인 |
| 8 | 서명 개인키 보관 위치(서버 전용), 키 교체 전략 부재, 로컬 캐시 파일 변조 가능성, replay로 구버전 정책 재전송 | 정상 서명 정책 적용 확인 + 서명 조작된 정책 거부 확인 + 네트워크 단절 상태에서 캐시 정책으로 동작 확인 |
| 9 | **진입 전제: 대시보드/운영자 인증 구현 완료(절대 규칙 2 Phase 9 진입 게이트).** WMI 이벤트 구독의 리소스 소모(프로세스 생성 폭주 시), 프로세스명 매칭은 우회가 쉬움(파일명 변경), 오탐 | Sandbox에서 테스트용 금지 프로세스 실행 → 감사 로그 기록 확인, 실제로는 종료되지 않는지 확인 |
| 10 | 보호 목록 누락 시 explorer.exe·보안 SW·에이전트 자신을 죽여 PC 먹통. 방화벽 규칙 오적용 시 자기 자신도 서버와 통신 불가(self-lockout) — 에이전트-서버 통신 포트는 항상 allow 예외 필수 | **반드시 Windows Sandbox(또는 스냅샷 가능한 VM)에서만** 테스트, 실제 메인 PC에서 Enforce 모드 테스트 절대 금지. 보호 프로세스 차단 시도 거부 확인, 긴급 해제 후 전원 Audit 복귀 확인, self-lockout 안 되는지 확인 |

---

## 🏗️ 아키텍처

- **언어/런타임**: C# / .NET 10 (LTS) — 설치된 SDK에 맞게 조정
- **IDE**: Visual Studio (Claude Code는 솔루션 루트의 터미널에서 실행)
- **DB**: SQLite (개발) → 필요 시 SQL Server
- **ORM**: EF Core / **테스트**: xUnit + FluentAssertions

```
NetSamLite.sln
src/
  NetSamLite.Shared/   # DTO, API 계약 (record 타입). 다른 프로젝트 참조 금지
  NetSamLite.Agent/    # Worker Service. 수집기(Collector) + 전송기(Sender)
    Collectors/        #   RegistrySoftwareCollector, WmiHardwareCollector
  NetSamLite.Server/   # ASP.NET Core Web API + EF Core
    Normalization/     #   Phase 3 정규화 규칙 엔진
    Licensing/         #   Phase 4 라이선스 매칭
  NetSamLite.Web/      # Phase 5 Blazor 대시보드
tests/
  NetSamLite.Agent.Tests/
  NetSamLite.Server.Tests/
samples/               # 테스트용 수집 결과 JSON (실제 개인 PC 데이터는 익명화 후 저장)
```

**의존 방향**: Agent → Shared ← Server ← Web. Agent와 Server는 서로 직접 참조하지 않는다.

---

## 📥 수집 항목 (이 표에 없는 값은 수집 금지)

**설치 SW** — 레지스트리를 읽는다
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
- `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` (64비트 OS의 32비트 앱)
- `HKEY_USERS\<SID>\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` — **그 순간 로드돼 있는 사용자 하이브 전체(읽기 전용)**, 64/32비트 뷰 둘 다. 서비스가 SYSTEM 계정으로 돌면 `HKCU`가 SYSTEM 자신의 프로필이 되어버려서, 단순 HKCU 대신 `HKEY_USERS`를 직접 열거해 로드된 모든 사용자(SID 패턴 `S-1-5-21-...`만, SYSTEM/서비스 계정/`.DEFAULT`/`_Classes`는 제외)의 Uninstall 키를 읽는다. 하이브를 추가로 로드/언로드하지 않음(이미 로드된 것만 조회) — 수집 시점에 로그인해 있는 사용자만 보인다는 한계가 있다(Phase 6).

| 값 | 용도 |
|---|---|
| DisplayName, DisplayVersion, Publisher | 식별 (DisplayName 없으면 제외) |
| InstallDate, InstallLocation | 이력 |
| SystemComponent = 1 / ParentKeyName 존재 | 제외 대상 (시스템 구성요소·업데이트) |

**하드웨어** — WMI
- `Win32_ComputerSystem` (제조사, 모델), `Win32_BIOS` (SerialNumber), `Win32_Processor` (Name, 코어 수)
- `Win32_OperatingSystem` (Caption, Version, BuildNumber), `Win32_PhysicalMemory` (용량 합계), `Win32_DiskDrive` (모델, 용량)

**장치 식별자**: BIOS 시리얼 + 컴퓨터 이름 조합. 시리얼이 비거나 "To be filled by O.E.M." 같은 더미면 MachineGuid(`HKLM\SOFTWARE\Microsoft\Cryptography`)로 대체.

---

## 📖 도메인 용어

- **Device**: 에이전트가 설치된 PC 한 대
- **RawSoftware**: 레지스트리에서 읽은 원본 그대로의 SW 항목
- **SoftwareProduct**: 정규화된 제품 (예: "Microsoft 365 Apps for enterprise - ko-kr" → `Microsoft 365 Apps`)
- **License**: 보유 라이선스 (제품, 유형, 수량, 만료일)
- **초과 설치**: 설치 수 > 보유 수량 / **미사용 라이선스**: 보유 수량 > 설치 수
- **금지 SW**: 정책상 설치되면 안 되는 제품 (알림 대상)

**데이터 흐름**: Agent 수집 → `POST /api/v1/inventory` → RawSoftware 저장 → 이전 스냅샷과 비교해 이력 기록 → 정규화 → 라이선스 매칭 → 리포트

---

## 🔨 빌드 / 테스트 / 실행

```powershell
dotnet build                                   # 전체 빌드
dotnet test                                    # 전체 테스트
dotnet run --project src/NetSamLite.Agent -- --once --out samples/my-pc.json   # 1회 수집
dotnet run --project src/NetSamLite.Server     # API 서버 (Phase 2~)
dotnet ef migrations add <이름> --project src/NetSamLite.Server   # DB 마이그레이션

# Server를 처음 실행하기 전, 로컬 DB 연결 문자열을 user-secrets로 설정 (appsettings.json에 넣지 않음 — 절대 규칙 4)
dotnet user-secrets set "ConnectionStrings:NetSamLite" "Data Source=netsamlite.db" --project src/NetSamLite.Server
```

- 서비스 등록(`sc.exe create`)은 Phase 6에서만, 실행 전 나에게 확인받는다 (관리자 권한 필요).

---

## ✍️ 코딩 컨벤션

- `Nullable` enable, file-scoped namespace, DTO는 `record`
- 비동기 메서드는 `Async` 접미사, `CancellationToken` 전달
- 로그는 `ILogger` 구조적 로깅 (`{DeviceId}` 같은 템플릿). `Console.WriteLine` 금지
- 수집기는 인터페이스(`ISoftwareCollector`) 뒤에 두고, 테스트는 가짜 구현으로 한다 (레지스트리에 직접 의존하는 테스트 금지)
- 정규화 규칙은 코드 하드코딩 대신 `normalization-rules.json` 데이터로 분리
- 테스트 이름: `메서드_상황_기대결과` (예: `Normalize_VersionSuffixInName_RemovesSuffix`)
- 커밋: Conventional Commits, 본문은 한국어 — `feat(agent): WOW6432Node 수집 추가`

---

## 🤖 서브에이전트 사용 규칙

- 기능 구현 후 → **code-reviewer** 로 리뷰 (특히 절대 규칙 1~5 위반 여부)
- 새 클래스 추가 후 → **test-writer** 로 테스트 작성·실행
- Phase 3 정규화 규칙 설계 시 → **normalizer-analyst** 로 `samples/` 데이터 분석

---

## ⚡ 트리거

- "phase 체크" → 현재 Phase 완료 기준 대비 진행 상황을 체크리스트로 보고
- "샘플 수집" → Agent를 `--once`로 실행해 `samples/`에 저장 후 항목 수·제외 수 요약
- "학습 정리" → 이번 세션에서 새로 배운 개념을 `docs/learning-log.md`에 날짜와 함께 추가

---

## 📝 백로그

지금 당장 안 고치기로 한 항목(성능 개선 등)은 `docs/backlog.md`에 기록한다. 코드 리뷰에서 🟡로 나왔지만 보류한 항목은 전부 여기 있어야 한다.
