# 통합 로그와 LogKit 적용

## 기록 경로와 책임

게임 분석 기록은 `LogSession` → `FileLogStore` 한 경로로 기록한다.
파일은 `Application.persistentDataPath/Telemetry/{sessionId}.jsonl`이다.
`LogGameLifecycle`이 세션·웨이브·사망을 관리하고 `LogCombatCollector`가 발사·적중·실제 피해·장비 사용 시간을 집계한다.
세션 ID, sequence, FixedUpdate 누적 시간은 모두 이 시스템 값을 사용한다.

기존 `GameTelemetry`의 별도 runId, Time.time 시계, Run/Wave/Combat/Player 이벤트 및 Count/Measure 호출은 제거했다.
게임 오버의 이중 종료와 투사체 적중의 이중 집계도 제거했다.
기존 지표를 분석할 때는 player_shot, projectile_impact, player_damage와 웨이브 요약을 사용한다.
같은 summaryId에서는 revision이 가장 큰 전체 요약만 채택한다.

`LogGameplayEvents`는 슬롯 요청/거부·구매·무기 이동 관측을 같은 LogSession에 기록하는 세션별 어댑터다.
5차의 `LogAmmoCollector`가 탄약 경제를, 6차의 `LogSlotCollector`가 성공한 회전의 spinId·시작/결과·미완료를 소유한다.
GameManager가 소유하므로 다른 게임 세션의 전역 상태를 참조하지 않는다. 시작 전·종료 후에는 기록하지 않는다.
현금·탄약·파츠 보상, 실제 슬롯 회복량, 구매 실패, 교체 무기, 탄창 정보가 유지된다.
구매 금액은 정가가 아닌 실제 잔액 감소량이므로 payload 필드는 `spentAmount`이다.
전투 이벤트에는 조준·아킴보·발사 직후 탄약, 개별 탄의 헤드샷·헬멧·경직 정보를 합쳤다.
기존 산포·반동·슬롯 보상 게임플레이는 유지한다.

## LogKit의 역할

`LogKitBootstrap`은 기존 TelemetryData 설정을 읽어 LogKit v0.3.0을 구성한다.
LogKit은 Unity 콘솔·오류 수집과 해당 LogKit 파일의 선택적 전송을 담당한다.
파일은 `Application.persistentDataPath/Logs`에 저장된다.
콘솔 파일과 분석 파일은 목적이 다르며 게임 사건을 양쪽에 다시 기록하지 않는다.

**분석 JSONL은 LogKit 업로드 대상에 자동으로 포함되지 않는다.**
분석 저장소를 대체하거나 서버 수집기를 연결하는 작업은 별도다. 이 변경에서는 분석 파일의 null·sequence·고빈도 관측·요약 revision 계약을 유지한다.

- 게임 분석 활성화 및 버퍼: `Resources/LogData.json`.
- LogKit 콘솔·서버 전송: `Resources/TelemetryData.json`.
- 로컬 전송 설정: Git 제외 `Resources/TelemetryLocalData.json`의 명시 필드만 덮어쓴다.
- 기존 `logShotEvents`, `logHitEvents`는 제거했다. 로컬 설정에 남은 항목은 무시된다.
- 서버 주소·키를 저장소에 추가하지 않는다. 기본 uploadEnabled는 false다.

## 사용자 Unity 검증

이 통합 작업에서는 Unity를 실행하지 않았다.
실제 게임에서 사격(산탄·조준·아킴보 포함), 헤드샷·헬멧·경직, 슬롯의 회복·현물 지급, 상점 구매 성공·실패, 무기 교체·획득, 사망·타이틀 복귀·재시작을 확인한다.
보상·피해·반동이 동일하고, 재시작 시 분석 sessionId가 바뀌며 종료 뒤 해당 파일에 추가 기록이 없어야 한다.
LogKit은 씬 재시작에도 한 인스턴스이고 콘솔 오류를 수집하는지 별도로 확인한다.
서버 전송은 로컬 설정을 지정한 환경에서 확인한다.

Play Mode를 종료한 뒤 `Tools > POC > Verify Unified Telemetry File`을 실행하면 가장 최근 분석 파일의 세션·sequence·종료·발사/탄 중복·요약 revision을 검사한다.
기존 구형 지표 전용 자동 플레이 검증기는 통합 파일 검사기로 교체했다. 이 메뉴는 게임을 실행하거나 씬을 변경하지 않는다.
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
구매 시도 purchaseAttemptId·상점 상세는 7차, 전체 운영 통합 검증은 8차다.
