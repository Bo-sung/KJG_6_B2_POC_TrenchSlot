# LogKit 게임 로그 적용

> POC2(Unity 6000.3.22f1)에 [LogKit](https://github.com/Bo-sung/logkit) v0.3.0을 붙인 방식과 설정법. 마지막 업데이트: 2026-10-31 (develop 2f80ba0 슬롯 보상·상점 타이밍 반영)

## 1. 설치 (버전 고정)
- `POC2/Packages/manifest.json`: `"com.team.logkit": "https://github.com/Bo-sung/logkit.git?path=package#v0.3.0"`
- `packages-lock.json`이 태그 커밋 `172a28918ebc44592a7b19e3e9f88a058264e81d`로 고정한다. 버전을 올릴 때는 태그만 바꾼다.
- LogKit은 `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`로 스스로 `GameLogger` 하나를 만들고(`DontDestroyOnLoad`) 씬이 바뀌어도 유지한다. 씬에 `GameLogger`를 배치하지 않는다.

## 2. 구조
| 파일 | 역할 |
|------|------|
| `Assets/Resources/TelemetryData.json` + `Data/TelemetryData.cs` + `Info/TelemetryInfo.cs` | 로그 설정 테이블(TableManager `Telemetry`) |
| `Scripts/Telemetry/GameTelemetry.cs` | **LogKit 결합은 여기만.** `BeforeSceneLoad`에서 JSON 설정을 `GameLogger.Configure`로 넣고, 게임 코드는 `GameTelemetry.*`만 호출 |
| `Scripts/Telemetry/TelemetryEvents.cs` | 이벤트 data 직렬화 자료(JsonUtility) |
| `Editor/PocTelemetryVerification.cs` | CLI 검증 |

- 로깅 예외는 모두 삼킨다. 서버가 없거나 파일 기록이 실패해도 게임은 그대로 진행한다.
- 파일 기록·전송은 LogKit 워커 스레드가 한다. 게임 쪽 호출은 이벤트 발생 시에만 있고 매 프레임 호출은 없다.

## 3. 로그 위치
- `Application.persistentDataPath/Logs/{세션ID}.jsonl` — 첫 줄 세션 헤더, 이후 한 줄에 로그 1건(`timestampUtc·level·category·scene·message·dataJson`)
- `{세션ID}.metrics.json` — Count/Measure 지표 스냅샷
- 에디터·Windows 기준: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\POC2\Logs\`
- 기본 설정(전송 꺼짐)에서는 파일이 지워지지 않고 쌓인다. 필요하면 직접 지운다. 저장소 밖이므로 Git에 들어가지 않는다.

## 4. 이벤트 (category / message)
모든 data에 `runId`(런마다 8자), `wave`, `runSeconds`가 붙는다.

| category | message | 주요 data |
|----------|---------|-----------|
| Run | RunStarted · GameOver · RunEnded | startMoney·maxHealth·startWeaponId / reason(GameOver·SceneUnloaded·Quit)·balance·totalKills·shots·shotsHit·damageTaken |
| Wave | WaveStarted · EnemiesCleared · WaveCompleted · IntermissionStarted | 적 구성·시간 / 전멸 시 남은 시간 / 실제 회복·보너스·잔액 / 정비 시간(`ShopDuration`) |
| Slot | SpinRequested · SpinRejected · SpinStarted · SpinResolved | cost·balance·reason(Spinning·WaveInactive·InsufficientFunds·ReelsBusy) / faces·spinSeconds·healthRestored(회전 시작 실제 회복)·health / multiplier·payout(배수 현금)·rewardCash·rewardAmmoId·rewardAmmoAmount·ammoRefill·rewardPartIds(실제 지급 현물)·잔액·탄약 |
| Shop | PurchaseSucceeded · PurchaseFailed | itemType(Weapon·Part·Stat·Ammo·SlotUpgrade)·itemId·reason·price·balanceBefore·balance |
| Weapon | WeaponBought · WeaponPickedUp · WeaponDropped | weaponId·slot·replacedWeaponId·magazine·reason(Bought·PickupSwap) |
| Combat | Shot · Hit · EnemyKilled | shotId·weaponId·hand·pellets·탄약 / 실제 투사체 피해·headshot·killed / enemyKind·reward·처치 수 |
| Player | PlayerDamaged | damage(방어 적용 후)·health·maxHealth·lethal |
| Unity | (Debug.Log 원문) | LogKit 자동 캡처(`captureUnityLogs`) |

지표(Count/Measure, 중복 없이 사건당 1회):
`RunStart`, `GameOver`, `GameOverWave`(M), `WaveStart`, `WaveClear`, `WaveComplete`, `SlotSpin`, `SlotHeal`(M, 회전 시작 실제 회복), `SlotWin`(배수 현금 당첨), `SlotReward`(현물 지급 있음), `SlotPayout`(M, 배수 현금), `ShopPurchase`, `ShopPurchaseFailed`, `ShopSpend`(M), `WeaponPickup`, `Attack`(방아쇠 1회, 산탄 전체 1회), `AttackHit`(적에게 피해를 준 발 — 한 발의 여러 산탄·콜라이더는 1회), `DamageDealt`(M), `Kill`(적 1명 사망 1회), `PlayerDamage`, `PlayerDamageTaken`(M). (M) = Measure

## 5. 설정 (`Resources/TelemetryData.json`)
| 키 | 기본 | 설명 |
|----|------|------|
| enabled | true | false면 게임 이벤트와 Unity 자동 캡처를 모두 끈다 |
| captureUnityLogs | true | Debug.Log 계열 자동 수집 |
| flushIntervalSeconds | 5 | 파일 기록 주기 |
| logShotEvents / logHitEvents | true | 발사·적중 상세 줄. 끄면 `Attack`/`AttackHit` 지표만 남는다 |
| uploadEnabled | **false** | 서버 전송. 꺼져 있으면 로컬 JSONL만 남기고 기동 재전송도 하지 않는다 |
| developmentServerUrl / releaseServerUrl | "" | 빈 값이면 LogKit 기본 `http://localhost:5000/api/logs`. 에디터·Development 빌드는 development, 릴리스 빌드는 release 주소 사용 |
| apiKey | `dev-log-key` | 공개된 개발용 기본 키. 운영 키는 로컬 설정으로 덮어쓴다 |
| uploadIntervalSeconds / requestTimeoutSeconds / retryPendingOnStartup | 5 / 5 / true | 전송이 켜졌을 때만 의미 있음 |

### 서버 URL·키 설정 (로컬 전용, 커밋 금지)
저장소 JSON에는 공개된 개발용 기본 키 `dev-log-key`만 둔다. 실제 서버 주소·운영 키는 `Assets/Resources/TelemetryLocalData.json`(Git 제외)에 넣으면 적힌 항목만 덮어쓴다.
```json
{
  "uploadEnabled": true,
  "developmentServerUrl": "http://localhost:5000/api/logs"
}
```
- 로컬 개발 서버는 LogKit 저장소의 `server`(ASP.NET Core 8)를 직접 실행한다. 서버 배포·방화벽·자동 실행 설정은 이 프로젝트 범위 밖이다.
- 이 파일은 그 PC에서 만든 빌드의 Resources에 포함된다. 실제 운영 키는 받기 전까지 넣지 않는다.
- 전송 실패 시 파일과 보낸 위치(`.progress.json`)가 남아 다음 주기·다음 기동에 이어서 보낸다. 다 보내면 LogKit이 로컬 파일을 지운다.

## 6. 검증 (Unity CLI)
```
Unity.exe -batchmode -nographics -projectPath <POC2> -executeMethod PocTelemetryVerification.RunChecks -logFile <log>
```
실제 Game 씬 플레이: 로거 1개(씬 재시작 후에도), 런 시작·게임 오버·재시작·종료 각각 1회, 웨이브·상점 성공/실패·처치·피격 기록, `AttackHit`/`Kill` 중복 방지, `logShotEvents` 억제, 아무도 듣지 않는 localhost 포트로 전송 실패해도 진행·로컬 파일 유지, 종료 flush 후 JSONL·metrics 내용을 확인한다.
슬롯은 회전 시작 실제 회복량, 회전 중 재진입 거절, 회전 중 웨이브 만료 → 정비(`ShopDuration`) 중 결과 지급 1회, 결과 줄의 `payout + rewardCash`가 실제 소지금 변화와 같은지까지 본다.

최근 결과(2026-10-31, develop 2f80ba0 병합 후 워크트리): `PocTelemetryVerification.RunChecks` exit 0, passes=58 failures=0.
- 같은 트리 회귀: `PocSlotHealVerification.Run` failures=0, `PocSlotRewardVerification.Run` failures=0, `PocShootingVerification.Run` failures=0, `PocProjectileVerification` failures=0, `PocVerification.RunUnitChecks` failures=0, `PocWaveRecoveryVerification.RunChecks` passes=55 / exit 1(실패 1건은 아래 에디터 Search 기동 예외, FAIL 체크 0건).
- develop의 `PocSlotRewardVerification`은 `[InitializeOnLoad]` + `delayCall`로 플레이 진입 리로드 때도 모델 검사를 돌려 `OnSpinStopped`를 직접 부른다. 그 결과 실제 회전 전 `Slot/SpinResolved` 1줄(wave 0)이 JSONL에 남는다. 검증은 이를 따로 세어 로그로 보고하고 실제 회전(SpinStarted 이후)만 판정한다.
- 배치 모드 기동 때 Unity 에디터 검색 인덱서(`UnityEditor.Search.SearchInit.IndexationOnStartup`)가 `ArgumentOutOfRangeException`을 한 번 던진다. 게임·LogKit 코드와 무관하며 이 검증만 그 스택을 제외한다(기존 `PocWaveRecoveryVerification`·`PocAkimboVerification`은 이 예외 1건을 실패로 센다). 예외 자체는 CLI 로그와 JSONL(`Unity` 카테고리)에 그대로 남는다.

## 7. 제약
- LogKit `GameLogger`는 정적 상태를 도메인 리로드로 초기화한다. Enter Play Mode에서 **Reload Domain을 끄면** 두 번째 플레이부터 로거가 생성되지 않을 수 있다(현재 설정은 리로드함).
- `BeforeSceneLoad`에서 TableManager를 처음 읽으므로 테이블 검증 로그는 LogKit 기동 전이라 JSONL에 남지 않는다(콘솔에는 남음).
- 종료 시 로그는 LogKit 종료 flush 전에 `RunEnded(Quit)`를 남긴다. 강제 종료·크래시 때는 마지막 flush 주기(기본 5초) 이후 로그가 빠질 수 있다.
