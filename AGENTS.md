# OptiSensor 작업 지침

## 빌드 기본값

사용자가 별도 형식을 지정하지 않고 “빌드해줘”라고 요청하면 다음 기준으로 수행한다.

- `Release` 설정으로 빌드한다.
- DLL 중심의 `dotnet build`만 완료로 보지 않는다.
- 실행 가능한 Windows EXE가 생성되도록 `dotnet publish`를 수행한다.
- 기본 대상 런타임은 `win-x64`다.
- 기본 출력은 `artifacts/release/OptiSensor.exe`다.
- 빌드 완료 보고에는 EXE의 절대 경로와 빌드 경고·오류 수를 포함한다.
