# NetSAM-Lite — SW 자산관리(SAM) 학습 프로젝트

PC에 설치된 SW/HW 정보를 에이전트로 수집하고, 서버에서 정규화·라이선스 비교·리포트를 하는 미니 SAM 솔루션.
목적은 **상용 SAM 솔루션의 동작 원리를 직접 구현하며 이해하는 것**이다. 완성도보다 이해가 우선.

---

## 🚫 절대 규칙 (위반 금지 — 최우선 적용)

1. **회사 자산 사용 금지**: 재직 중인 회사의 소스코드, 내부 문서, SW DB, 고객 데이터를 참조·복사·추측 재현하지 않는다. 공개 문서(Microsoft Docs 등)와 직접 구현만 사용한다.
2. **에이전트는 읽기 전용**: 레지스트리·WMI는 조회만 한다. 레지스트리 쓰기, SW 삭제, 프로세스 종료 코드는 작성하지 않는다. (정책 기능은 "알림"까지만)
3. **개인정보 수집 금지**: 아래 「수집 항목」 표에 있는 값만 수집한다. 사용자 파일, 브라우저 기록, 계정 비밀번호, 화면은 절대 수집하지 않는다.
4. **비밀값 커밋 금지**: 연결 문자열, API 키, 인증서는 `dotnet user-secrets` 또는 환경변수로만 다룬다. `appsettings.json`에 넣지 않는다.
5. **`Win32_Product` WMI 클래스 사용 금지**: 조회 시 MSI 무결성 검사·재구성이 일어나 느리고 부작용이 있다. 설치 SW는 레지스트리 Uninstall 키로 수집한다.
6. **현재 Phase 밖 기능 구현 금지**: 아래 「진행 단계」의 현재 Phase 범위만 작업한다. 앞 단계 기능이 필요하면 먼저 묻는다.
7. **계획 먼저, 코드는 나중**: 파일 3개 이상 수정하거나 새 프로젝트를 추가할 때는 수정할 파일 목록과 이유를 먼저 제시하고 승인받는다.
8. **빌드·테스트 통과 전 완료 보고 금지**: `dotnet build`와 `dotnet test`가 통과해야 "완료"라고 말한다.
9. **설명하며 구현**: 나는 학습 중이다. 새로운 API·패턴을 쓰면 왜 그걸 골랐는지 2~3줄로 설명한다.

---

## 🎯 진행 단계 (현재: Phase 6)

| Phase | 목표 | 완료 기준 |
|---|---|---|
| 1 | 수집 에이전트 (콘솔 실행) | `--once` 실행 시 설치 SW·HW 정보를 JSON 파일로 저장 |
| 2 | 서버 API + DB | 에이전트가 POST → DB 저장, 변경 이력(설치/삭제) 기록 |
| 3 | SW 정규화 | 원본 이름·버전을 `SoftwareProduct`로 묶는 규칙 엔진 + 테스트 |
| 4 | 라이선스 매칭 | 보유 수량 vs 설치 수 비교, 초과/미사용 판정 |
| 5 | 대시보드·정책 | Blazor 화면, 금지 SW 알림, CSV 리포트 |
| **6** | 서비스화 | Windows 서비스 등록, 주기 수집, 재시도·로그 |

Phase가 끝나면 이 표의 "현재" 표시를 갱신한다.

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
