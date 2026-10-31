# WW1 총기 에셋 적용 (P08 · SMLE · MP18 · M1897)

> 작성 2026-10-31. **컴파일만 확인했다.** Unity 임포트, 프리팹 표시, 정조준, 플레이는 확인하지 않았다(§5).

## 1. 원본과 프로젝트 파일

| 무기 id | 외형 id(`modelId`) | 원본 (`C:\Gits\assetFactory\runs\…`) | 프로젝트 FBX (`Assets/Models/AssetFactory/`) | 표시 메시 |
| --- | --- | --- | --- | --- |
| p08 | `luger_p08` | `luger_p08_v8/luger_p08_v8_game.fbx` | `luger_p08_v8_game.fbx` (원본 그대로) | LOD0 `luger_p08_v8` (916 tri) |
| smle | `smle_mk3` | `smle_mk3_v7/smle_mk3_v7_reference_game.fbx` | `smle_mk3_v7_reference_game.fbx` (원본 그대로) | LOD0 `smle_mk3_v7_reference` (2168 tri) |
| mp18 | `mp18` | `mp18_v5/mp18_v5_reference.fbx` | `mp18_v5_reference_unity.fbx` (다시 내보낸 사본, §2) | 부품 102개 (3244 tri) |
| m1897 | `winchester_1897` | `winchester_1897_v7/winchester_1897_v7_game.fbx` | `winchester_1897_v7_game.fbx` (원본 그대로) | LOD0 `winchester_1897_v7` (2408 tri) |

- `.fbx.meta`는 기존 `colt_1878_shotgun_reference_sightfix.fbx.meta` 설정을 그대로 쓰고 GUID만 새로 만들었다.
- 기존 임시 외형(`revolver`·`bolt`·`smg`·`shotgun`)은 지우지 않았다. WW2 무기와 드롭 대체 외형(`fallbackModelId: bolt`)이 계속 쓴다.

## 2. LOD와 MP18 처리

- **LOD0만 쓴다.** game FBX에는 LOD1·LOD2와 `UCX_*` 볼록 껍질이 같이 들어 있다. 새 컴포넌트 `WeaponModelLod0`가 `Awake`에서 `_LOD1`·`_LOD2`·`UCX_` 노드를 끄고 LODGroup도 끈다. 근거:
  - Luger LOD2에는 후단 가늠자 귀가 없어 정조준 노치가 사라진다(`assetFactory/docs/firearms-ww1-final-lod-sight-review.md` §1.3).
  - M1897 LOD1에는 두께 없는 판 10개가 있다(같은 문서 §2.4).
  - UCX는 Unity가 충돌체로 바꾸지 않는다(`addColliders: 0`). 끄지 않으면 회색 메시로 그려진다. 픽업 충돌은 기존처럼 BoxCollider를 쓴다.
  - 에디터 프리팹 미리보기에서는 하위 LOD와 UCX가 겹쳐 보인다. 끄는 시점이 런타임 `Awake`라서 그렇다.
- **MP18은 game FBX를 쓰지 않았다.** `mp18_v5_reference_game.fbx`는 소켓 이름이 `RearSight.001`처럼 바뀌어 있다. 소켓 위치도 LOD0 원점만큼 두 번 밀려 있다(FrontSight x 0.8465, 기준 FBX는 0.3925). 이대로면 `WeaponHeldModels.GetSightPoints`가 소켓을 찾지 못한다.
- MP18 기준 FBX도 쓰기 전에 다시 내보냈다. 원본은 다른 총기와 내보내기 축이 다르다(`axis_up=Z`, `bake_space_transform=False`).
  - Blender 5.2.3 `-b --factory-startup`으로 임포트하고, 다른 총기와 같은 `FBX_KW`(`bake_space_transform=True`, `FBX_SCALE_ALL`)로 다시 내보냈다.
  - 다시 임포트해 비교했다. 객체 108개의 월드 위치, bbox, 삼각형 수, 정점 합이 원본과 모두 같았다. 원본 `runs/`는 수정하지 않았다.

## 3. 배치 (HeldWeapons · Pickup)

- 기존 `colt_1878_shotgun`과 같은 구조로 배치했다. 외형 GameObject(`WeaponModelLod0` 포함) 아래에 FBX 중첩 프리팹을 두고, Y 90° 회전으로 총구를 +Z에 맞췄다.
- 위치는 FBX 루트 기준 `(0, -세로중심, 앞뒤)`다.

| 외형 id | 세로중심 | 앞뒤 | 가늠자 높이 | 총구 z | 조준 eyeDistance |
| --- | --- | --- | --- | --- | --- |
| luger_p08 | 0.131 | -0.07 | +0.010 | +0.152 | 0.25 |
| smle_mk3 | 0.115 | -0.52 | +0.010 | +0.600 | 0.38 |
| mp18 | 0.2045 | -0.39 | +0.010 | +0.420 | 0.30 |
| winchester_1897 | 0.011 | -0.40 | +0.010 | +0.600 | 0.28 |

- **좌우 방향:** Blender −Y(총의 오른쪽)가 Unity +X로 간다. SMLE 노리쇠 손잡이는 Blender 상면 렌더에서 −Y 쪽에 있다. MP18 탄창 하우징은 +Y(왼쪽)에 있다. 거울 반전은 없다.
- **크기:** 모두 1:1 미터다. 크기 보정은 하지 않았다.
- **정조준:** `WeaponAimData.json`에 네 외형의 프로필을 추가했다. 런타임에서는 FBX의 `RearSight`·`FrontSight` 소켓을 우선한다. 테이블 rear/front는 소켓이 없을 때 쓰는 값이고, 같은 소켓 좌표에서 계산했다. eyeDistance는 개머리판이 눈 뒤 0.12–0.22 m에 오도록 잡은 **가설값**이다.
- **아킴보:** P08만 `akimboAllowed`라서 `luger_p08_OffHand`를 추가했다. 위치 x −0.5는 기존 왼손 모델과 같다.
- **픽업:** `Pickup_luger_p08` · `Pickup_smle_mk3` · `Pickup_mp18` · `Pickup_winchester_1897`을 추가하고 `WeaponPickupCatalog`에 등록했다.
  - 모델은 가장 낮은 점이 바닥 위 1 cm에 오게 올렸다.
  - BoxCollider는 모델 bbox에 2 cm 여유를 더한 크기다.
- 프리팹은 열린 에디터를 건드리지 않으려고 YAML로 직접 추가했다. `PocWeaponDropAuthoring`도 같은 값으로 고쳤다(`MODEL_IDS`, `BuildModel`, `PickupModelHeight`, `BoundsCenter`, `BoundsSize`). 나중에 `AuthorAll`을 돌려도 같은 배치가 나온다.

## 4. 지원하지 않는 것 / 남은 것

- **파츠 장착 없음.** `PartData.json`에 p08·smle·mp18·m1897 파츠가 없다. AssetFactory에도 이 네 총기의 파츠 키트(`firearms_parts_poc` 형식)가 없다. 그래서 `WeaponPartVisuals.json`에 추가하지 않았다.
- **쓰지 않는 소켓:** `Slot_Muzzle`, `Slot_Magazine`(MP18), `Slot_Bayonet`(M1897), `PistolGrip`. 발사 위치는 지금처럼 `PlayerWeapon.MUZZLE_OFFSET` 고정값을 쓴다.
- **애니메이션 없음.** 노리쇠, 토글, 펌프, 장전 동작은 없다. 원본 FBX에 리그나 애니메이션이 없다.
- **MP18 탄창:** 모델은 Snail 드럼 하우징 형태다. 게임 탄창 수 32는 바꾸지 않았다.
- **M1897:** 트렌치 건(방열판·총검 어댑터) 형태다. 총검 소켓은 쓰지 않는다.
- **적 WW1 소총 외형:** `Enemy.prefab`의 `wwi_rifle`은 아직 이전 `bolt` 사본이다. 에디터에서 `POC/Connect Enemy Weapon Models`를 실행해야 SMLE로 바뀐다. 이번에는 실행하지 않았다.
- **검증 코드 기대값:** `PocAimVerification`의 외형 짝, `PocWeaponDropVerification`의 아킴보 왼손 외형 id를 새 외형 id로 바꿨다. 실행하지는 않았다.

## 5. 확인 범위

- 컴파일 확인: Unity가 만든 csproj를 스크래치 폴더로 복사했다. Compile 목록만 현재 디스크의 `.cs`로 바꿨다.
  - 실행 명령: `dotnet build Assembly-CSharp-Editor.csproj --no-incremental`
  - 결과: Runtime 105개, Editor 33개 파일. **오류 0**, 경고 12. 경고는 모두 기존 `PocWeaponPartsAuthoring`의 CS0649다.
- **하지 않은 것:** Unity 임포트 결과, 프리팹 열기, 에디터·플레이 검증 메뉴, 화면 확인.

### 수동 확인 목록 (플레이 전)
1. Unity가 네 FBX를 임포트한 뒤 경고가 없는지 본다. 프리팹 4종과 HeldWeapons에 Missing Prefab이 없는지 본다.
2. 1인칭에서 각 무기를 들었을 때 총구 방향(+Z)이 맞는지 본다. 위아래·좌우 뒤집힘이 없는지(SMLE 노리쇠가 오른쪽인지) 본다. 크기와 손 위치가 자연스러운지 본다.
3. 우클릭 정조준에서 가늠자와 가늠쇠가 화면 중앙에 정렬되는지 본다. eyeDistance가 적절한지 본다. Luger 노치가 보이는지 본다(LOD0 고정).
4. 플레이 중 LOD1·LOD2·UCX 회색 메시가 보이지 않는지 본다.
5. 바닥 드롭과 줍기에서 모델이 바닥 위에 놓이는지 본다. BoxCollider 범위가 맞는지 본다.
6. P08 아킴보에서 왼손 Luger가 나오는지 본다.
7. 필요하면 `POC/Connect Enemy Weapon Models`로 적 SMLE 외형을 갱신한다.
