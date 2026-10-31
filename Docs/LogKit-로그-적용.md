# 통합 로그와 LogKit 적용

## 기록 경로와 책임

**[2026-10-31 스캔 개정]** 게임 분석 기록은 `LogSession` → `LogKitStore` 한 경로다. 과거 `FileLogStore`(persistentDataPath/Telemetry/{sessionId}.jsonl, 판별 파일)는 커밋 da15dea에서 제거됐고, 분석 줄은 `GameLogger.Data("Info","GameEvent",eventType,JSON)`로 LogKit에 합류한다. 물리적 파일은 LogKit이 소유 — `persistentDataPath/Logs/{LogKit세션ID}.jsonl`에 콘솔 수집과 **한 파일 혼합 기록**이며, 파일 경계는 앱 실행(LogKit 세션) 단위다. 게임 sessionId는 파일명이 아니라 각 레코드의 sessionId 필드로만 남고, 분석 레코드의 sequence·FixedUpdate 누적 시간은 LogSession 값 유지.
`LogGameLifecycle`이 세션·웨이브·사망을 관리하고 `LogCombatCollector`가 발사·적중·실제 피해·장비 사용 시간을 집계한다.

기존 `GameTelemetry`의 별도 runId, Time.time 시계, Run/Wave/Combat/Player 이벤트 및 Count/Measure 호출은 제거했다.
게임 오버의 이중 종료와 투사체 적중의 이중 집계도 제거했다.
기존 지표를 분석할 때는 player_shot, projectile_impact, player_damage와 웨이브 요약을 사용한다.
같은 summaryId에서는 revision이 가장 큰 전체 요약만 채택한다.

`LogGameplayEvents`는 슬롯 요청/거부·무기 이동 관측을 같은 LogSession에 기록하는 세션별 어댑터다.
5차의 `LogAmmoCollector`가 탄약 경제를, 6차의 `LogSlotCollector`가 성공한 회전의 spinId·시작/결과·미완료를 소유한다.
GameManager가 소유하므로 다른 게임 세션의 전역 상태를 참조하지 않는다. 시작 전·종료 후에는 기록하지 않는다.
현금·탄약·파츠 보상, 실제 슬롯 회복량, 구매 실패, 교체 무기, 탄창 정보가 유지된다.
구매 금액은 정가가 아닌 관측 구간의 실제 순잔액 감소량이므로 현재 `shop_purchase_result.payload.actualSpent`를 사용한다.
전투 이벤트에는 조준·아킴보·발사 직후 탄약, 개별 탄의 헤드샷·헬멧·경직 정보를 합쳤다.
기존 산포·반동·슬롯 보상 게임플레이는 유지한다.

## LogKit의 역할

`LogKitBootstrap`은 기존 TelemetryData 설정을 읽어 LogKit **v0.6.0**(Git 태그 참조, 커밋 efc8f13)을 구성한다. Preload 씬의 `PreloadController`에 설정한 프로필이 있으면 그 값이 Resources 설정을 덮어쓴다.
LogKit은 Unity 콘솔·오류 수집과 파일 보관·서버 전송을 담당한다.
파일은 `Application.persistentDataPath/Logs`에 저장된다.
[2026-10-31 개정] 콘솔 수집과 게임 분석(GameEvent)은 **같은 LogKit 파일에 혼합 기록**된다. 분석 줄은 `GameLogger.Data("Info","GameEvent",eventType,JSON)` — eventType은 message, JSON 줄 전체는 dataJson에 들어간다.

**[2026-10-31 개정] 분석 이벤트는 LogKit 전송 대상에 포함된다.** LogKitStore로 넘겨진 줄은 LogKit 파일에 보관되고 주기/종료 전송(FlushNow는 메인 타이머 차단 우회 즉시 전송) 대상이 된다. 저장된 `TelemetryData.json`의 uploadEnabled=false가 기본이지만, 저장소에 커밋된 `Resources/TelemetryLocalData.json`이 uploadEnabled=true와 개발 서버 주소(`http://172.21.10.67:5000/api/logs`)로 덮어쓴다.

- 게임 분석 활성화 및 버퍼: `Resources/LogData.json`.
- LogKit 콘솔·서버 전송 기본값: `Resources/TelemetryData.json`.
- 빌드 프로필: `Preload` 씬의 `PreloadController.telemetryProfile`이 `TelemetryData` 값을 덮어쓴다. `Awake`에서 적용되어 LogKit 로거 생성 전에 반영된다.
- 로컬 전송 설정: `Resources/TelemetryLocalData.json`의 명시 필드만 덮어쓴다. **Git 제외 계획과 달리 현재 저장소에 커밋돼 있다** — uploadEnabled=true, 서버 주소 `http://172.21.10.67:5000/api/logs`(dev 키 별도 커밋 2fdae95). 저장소에 주소가 노출되어 있으므로 "주소·키는 저장소에 추가하지 않는다"는 원칙은 현행 위반 상태다. Preload 프로필이 있으면 그 뒤에 Preload 값이 최종값이 된다.
- 기존 `logShotEvents`, `logHitEvents`는 제거했다. 로컬 설정에 남은 항목은 무시된다.

## Preload 빌드 프로필

`Preload` 씬의 `PreloadController`는 `TelemetryProfile`을 직렬화 필드로 가진다. 여기서 LogKit 사용 여부, 콘솔 수집, flush/upload 주기, 개발/릴리스 서버 주소, API 키, 재전송, 타임아웃을 설정한다.

적용 순서:
1. `Resources/TelemetryData.json` 로드
2. `Resources/TelemetryLocalData.json`가 있으면 덮어씀
3. `PreloadController.Awake`에서 Preload 프로필 덮어씀 (최종값)
4. `LogKitBootstrap.Apply`로 LogKit에 전달

Preload 씬이 없거나 Title/Game 씬을 직접 플레이하면 1~2단계 값이 fallback으로 쓰인다. 빌드 프로필을 나누려면 프로필별 Preload 씬을 만들고 Unity Build Profile의 Scene List에서 해당 씬을 첫 번째로 지정한다.

Preload 씬에 LogKit `GameLogger` 컴포넌트를 따로 두지 않는다. 별도 컴포넌트가 먼저 초기화되면 `RetryPendingUploads`가 프로필 적용 전 기본값으로 실행될 수 있다. 설정은 `PreloadController.telemetryProfile` 하나로 모은다.

## 사용자 Unity 검증

이 통합 작업에서는 Unity를 실행하지 않았다.
실제 게임에서 사격(산탄·조준·아킴보 포함), 헤드샷·헬멧·경직, 슬롯의 회복·현물 지급, 상점 구매 성공·실패, 무기 교체·획득, 사망·타이틀 복귀·재시작을 확인한다.
보상·피해·반동이 동일하고, 재시작 시 분석 sessionId가 바뀌며 종료 뒤 해당 파일에 추가 기록이 없어야 한다.
LogKit은 씬 재시작에도 한 인스턴스이고 콘솔 오류를 수집하는지 별도로 확인한다.
Preload 씬의 `PreloadController` Inspector에 `TelemetryProfile` 값이 보이고, Play 시 해당 주소/키로 전송되는지 확인한다.
서버 전송은 로컬 설정을 지정한 환경에서 확인한다.

Play Mode를 종료한 뒤 상점 로그는 `POC > Verify Shop Telemetry`(PocShopLogVerification), 입력 경로는 `POC > Verify Shop UI (Play)` 끝의 텔레메트리 검사로 확인한다.
과거 `Tools/POC/Verify Unified Telemetry File`(PocTelemetryVerification)은 LogKitStore 전환(da15dea)과 함께 **제거**됐다 — 로컬 파일 기반 검사기가 LogKit 파일 구조와 맞지 않기 때문. 이 메뉴는 게임을 실행하거나 씬을 변경하지 않았다.
파일 검사만으로 실제 보상·피해의 정확성까지 입증하지는 않으므로 위 게임플레이 확인이 필요하다.

## Unity 외부 회귀 검사

`.omo/verification/log-phase1~4`는 기존 로그 계약 회귀 검사다.
`.omo/verification/log-merge/LogMerge.csproj`는 기존 슬롯·구매 관측과 전투 상세 정보가 한 세션·sequence에 기록되며 중복 집계가 없는지 검사한다.
합성 예시는 `Docs/로그-시스템-통합.synthetic.jsonl`이며 실제 플레이 데이터가 아니다.

2026-10-31 검사 결과: 기존 1~4단계 9,326개 및 통합 312개 검사 통과.
패키지 잠금 커밋 `172a28918ebc44592a7b19e3e9f88a058264e81d` 소스와 함께 런타임·에디터 코드를 .NET 컴파일해 오류 0개를 확인했다.
인스펙터 할당 필드·기존 에디터 자료 관련 경고는 런타임 103개, 에디터 포함 115개다.
재현 명령은 `.omo/verification/log-merge/README.md`에 기록했다.

후속 5~6차에서 탄약 경제 요약과 슬롯 spinId·실제 지급·미완료 마감을 구현했다.
슬롯 결과는 실제 ammo_changed를 transactionId로 참조하며 획득량을 다시 집계하지 않는다.
6차 검사는 112개, 현재 통합 회귀는 320개가 통과했다. 상세는 `Docs/로그-시스템-6차-계약.md` 참조.
7차에서 LogShopCollector의 offerSetId/revision·구매 attemptId/결과·실제 생성/효과·탄약 원본 참조를 구현했다.
유료 회복은 현행 상점/API에 없어 복원하지 않았다. 실제 표시 행·스크롤 범위와 직접 API 호출을 구분한다.
7차 순수 검사 100개와 통합 회귀 330개가 통과했다. 상세는 `Docs/로그-시스템-7차-계약.md` 참조.
8차에서 전체 순수 수집기와 실제 저장(FileLogStore, 현 LogKitStore의 전신)을 연결한 통합 검사·파일 읽기 검사·운영 자료를 추가했다.
현재 요구사항 대응표·파일 해석·실제 Unity 사용자 확인표는 `Docs/로그-시스템-운영-검증.md`, 대표 합성 출력은 `Docs/로그-시스템-8차.synthetic.jsonl`이었다.
위 단계별 결과는 각 차수 당시 기록이며, 8차의 현재 소스 재실행 결과는 `.omo/verification/log-phase8/RESULTS.md`를 기준으로 봤다. 실제 Unity 확인은 사용자 대기다.
**[2026-10-31 스캔] 참조 산출물 실재 확인**: `Docs/로그-시스템-운영-검증.md`·`로그-시스템-6차/7차-계약.md`·각 synthetic.jsonl·`.omo/verification/*`는 현재 저장소에 **존재하지 않는다**(gitignore 또는 미커밋). 문서 참조는 읽기 전용 배경으로만 유효.
