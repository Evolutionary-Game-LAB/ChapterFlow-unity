# ChapterFlow Unity Runtime 0.1.1

웹에서 내보낸 ChapterFlow JSON을 Unity에서 읽고, 게임 행동에 따라 심볼과 사건 진행을 관리하는 오프라인 플러그인입니다. 런타임 LLM/API 키는 필요 없습니다. 현재 준비 대상은 Unity이며 Unreal 플러그인은 포함하지 않습니다.

## 설치와 예제

Unity Package Manager에서 **Install package from git URL** (버전에 따라 **Add package from git URL**)을 선택하고 다음 주소를 입력합니다. 저장소에 패키지 업로드가 완료된 후 사용할 수 있습니다.

```text
https://github.com/Evolutionary-Game-LAB/ChapterFlow-unity.git
```

로컬 폴더로 설치하려면 **Install package from disk**에서 이 폴더의 `package.json`을 선택합니다. 샘플 `ChapterFlow Quick Start`를 Import하고 `Tools > ChapterFlow > Create Quick Start Scene`으로 샘플 씬을 만듭니다.

챕터를 직접 생성하려면 [ChapterFlow-generator](https://github.com/Evolutionary-Game-LAB/ChapterFlow-generator)를 참조하세요. 플러그인 문제는 [Unity Issues](https://github.com/Evolutionary-Game-LAB/ChapterFlow-unity/issues)에 남길 수 있습니다.

웹의 JSON Export 파일은 `Tools > ChapterFlow > Import Web JSON…`으로 가져옵니다. 생성된 `.chapterflow` 에셋을 `ChapterRunner`의 Chapter에 지정합니다. `.json` TextAsset을 Chapter Json에 직접 지정할 수도 있습니다.

지원 선언: Unity 2022.3 이상, C# 9. 의존성은 Unity 공식 `com.unity.nuget.newtonsoft-json` 3.2.2입니다. 지원 선언과 실제 검증 범위는 다릅니다. 아래 검증 범위를 먼저 확인하세요.

## 게임 연결

```csharp
// 실제 챕터의 signal ID와 심볼 ID/args를 사용합니다.
string symbolKey = ChapterFlow.ChapterDefinition.SymbolKey("evidence");
runner.TrySendSymbolGameplay("read_evidence", symbolKey);
// 종료와 다음 시작은 엔진에서 별도 제어합니다.
runner.EndCurrentEvent();
runner.StartEvent("start:next_event");
```

| API | 의미 |
|---|---|
| `TrySendSymbolGameplay(signalId, symbolKey)` | 현재 신호가 허용한 **해당 심볼만** 변경. 웹의 심볼별 실행 버튼과 동일 |
| `TrySendGameplay(signalId)` / `SendGameplay(signalId)` | 해당 신호의 남은 효과 전체 실행. 기존 통합 게임 행동 호환 |
| `EndCurrentEvent()` / `EndEvent(id)` | 현재 사건 종료. 다음 사건으로 자동 이동하지 않음 |
| `StartEvent(trigger)` | 연결/진입/결말 조건을 검사한 뒤 시작 |
| `Session.GetAvailableEvents()` | 현재 상태 기준으로 종료 후 진입 가능한 사건 조회 |
| `CaptureSaveJson()` / `RestoreSaveJson(json)` | 진행 상태 문자열 저장/복원 |

`Session.ReceiveGameplaySignal(id, key)`와 `Session.GetSignalBlocker(id, key)`도 같은 단일 심볼 선택을 지원합니다. 심볼 키를 지정하지 않으면 기존 묶음 신호 방식입니다. 허용되지 않은 심볼이나 임의의 값을 넣어 상태를 바꿀 수 없습니다.

여러 효과 중 하나를 실행했다고 나머지가 완료 처리되지 않습니다. 부분 실행 뒤 묶음 실행을 해도 이미 적용된 일회성 숫자 증감을 중복 적용하지 않습니다. 부분 실행 이력은 저장/복원됩니다. 신호의 다른 사전 조건과 되돌림 불가 규칙은 유지합니다.

`OnEventStarted`, `OnEventEnded`, `OnGameplayReceived`, `OnStateChanged`, `OnEndingReached`, `OnSessionRestored`, `OnError` 이벤트로 게임 오브젝트/연출/UI를 연결합니다. `ChapterEventBinding`은 특정 사건 시작·종료를 UnityEvent에 연결합니다.

## 웹과의 의도된 차이

웹은 편의를 위해 다음 사건 시작 버튼에서 현재 사건 종료와 다음 사건 시작을 함께 처리합니다. 플러그인은 두 명령을 별개로 유지합니다. 웹의 빈 START 게이트 자동 통과와 카메라/노드 강조는 UI 동작이며 엔진 규칙이 아닙니다. 동일한 명령 순서를 실행하면 심볼·허용 사건·결말 판정이 같아야 합니다.

`gameplay.events[].signals`와 조건이 실행 기준입니다. 설명 문장으로 새 효과를 추측하지 않습니다. `requiredSignal`은 기존 생성 경로 추적용이며 실제 진입은 현재 상태로 판단합니다. `CONTRACT.md`를 함께 읽으세요.

## 저장 호환

`chapterflow-unity-save-v1`에 선택적 `partialResults` 필드를 추가했습니다. 이전 저장은 계속 읽을 수 있습니다. 0.1.1에서 부분 실행한 새 저장은 0.1.0으로 불러오지 마세요. 챕터 fingerprint가 달라지면 이전 진행 불러오기를 거부합니다. 저장은 게임 월드 오브젝트를 자동 복원하지 않으므로 `OnSessionRestored`에서 별도로 동기화합니다.

## 검증

2026-11-03 현재 웹 런타임과 저장된 기차역/인질 챕터를 **묶음 실행 + 심볼별 실행**으로 비교했습니다. 24개 시나리오, 446개 명령, 매 단계 저장/복원 446회 및 기존 코어 계약 검사 53개가 통과했습니다. 새 LLM 호출 없이 저장 결과를 사용했습니다.

독립적으로 실행할 공개 합성 테스트:

```sh
dotnet run --project Verification~/Parity.csproj -- Verification~/fixtures/parity-cases.json Samples~/QuickStart/signal-lab.json
```

검증 도구는 .NET 10 SDK가 필요하며 Unity 패키지 자체가 .NET 10을 요구하는 것은 아닙니다. 인질 챕터 등 비공개 실험 파일은 공개 저장소에 넣지 않았습니다. Unity 6000.6.6f1의 별도 임시 프로젝트에서 실제 패키지 임포트와 Play 모드 Runner 시험도 통과했습니다(챕터 4개, 명령 14개, 저장/복원, 상태·시작·결말 이벤트). Player/IL2CPP 및 다른 Unity 버전은 미검증입니다.

## 라이선스

이 프로젝트는 [Apache License 2.0](LICENSE)에 따라 배포됩니다. 외부 의존성은 각 패키지의 라이선스를 따릅니다. [THIRD-PARTY.md](THIRD-PARTY.md)를 참조하세요.
