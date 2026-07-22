program LegacyOracle;

{$mode Delphi}{$H+}
{$apptype console}

{$ifndef V78_SERIAL_RANGE_ERRORS}
  {$fatal V78_SERIAL_RANGE_ERRORS must be defined}
{$endif}

uses
  InterfaceBase,
  NoGUIInt,
  NoGUIWSFactory,
  Forms,
  LCLType,
  Types,
  Controls,
  StdCtrls,
  Menus,
  Spin,
  ExtCtrls,
  fpjson,
  jsonparser,
  WSLCLClasses,
  WSControls,
  WSForms,
  WSExtCtrls,
  WSStdCtrls,
  WSMenus,
  SysUtils,
  Classes,
  IniFiles,
  Math,
  Ini,
  DXCC,
  Main,
  Contest,
  Station,
  MyStn,
  DxStn,
  QrmStn,
  RndFunc,
  MorseKey,
  SndTypes,
  WavFile,
  VolmSldr;

const
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v78';
  ExpectedSource =
    'tests/parity/legacy-oracle/v78/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v78/build-recipe.json';
  ExpectedScenario = 'settings.legacy-serial-range-errors';
  ExpectedSampleRate = 11025;
  ExpectedBlockSize = 512;
  ExpectedSeed = 12345;
  ExpectedBandwidthHz = 500;
  ExpectedPitchHz = 600;
  ExpectedStartupRequestCount = 5;
  ExpectedComparedBlockCount = 1;
  ExpectedValueCount = 10;
  ExpectedRuntimeBandwidthValueCount = 7;
  ExpectedRuntimeRitValueCount = 7;
  ExpectedProbeCount = 12;
  ExpectedQrmTriggerOrdinal = 1024;
  ExpectedCleanTerminalOrdinal = 1024;
  ExpectedQrmTerminalOrdinal = 1033;
  ExpectedCatalogCount = 46039;
  ExpectedCatalogIndex = 23903;
  ExpectedCatalogCall = 'LU5MT';
  ExpectedMasterDataLength = 1239476;
  ExpectedMasterDataSha256 =
    'acf37090e7c9c0f2146a2b08608295cb243c8bfe649a421d1c528a59656097aa';
  ExpectedR1Bits = '3f03301e';
  ExpectedPatience = 2;
  ExpectedAmplitudeBits = '4698fe9d';
  ExpectedPitchOffsetHz = -124;
  ExpectedWordsPerMinute = 31;
  ExpectedMessageChoice = 2;
  ExpectedMessageSet = '[msgQrl2]';
  ExpectedMessageText = 'QRL?   QRL?';
  ExpectedEnvelopeSamples = 53248;
  ExpectedEnvelopeBlocks = 104;
  ExpectedSendPosition = 512;
  ExpectedCleanTerminalBits = '38e1bf40';
  ExpectedQrmTerminalBits = '3f519e01';
  ExpectedFirstDivergence = 310;
  ExpectedCleanBlockSha256 = '3ba44162f2959aeeaa6599059e97033d942258e3a2e0dc33cbb07defbb12a4c5';
  ExpectedQrmBlockSha256 = '72f7618e7e055db7fefd472c47f0488046087905d5810b1e1aa97b88187f643d';
  ExpectedConstructorSequenceBits =
    '38e1bf40,3f03301e,3eac999c,3f04e9ec,3f155543,3e293bc8,3f2dadc6,3da2d42c,3e9941cd,3f519e01';
  ExpectedCleanProbeBits =
    '00000000,00000000,00000000,00000000,00000000,00000000,00000000,b872a3f3,bbd71d89,bca8eae3,bcc4e706,bcc30643';
  ExpectedQrmProbeBits =
    '00000000,00000000,00000000,00000000,00000000,00000000,00000000,b83226a8,3baaf78e,3db87ddf,3e29dff0,3e6bb9e3';
  ExpectedStationCall = 'W7SST';
  ExpectedLocalMessage = 'CQ W7SST TEST';
  ExpectedMonitorLevelDb = 0;
  ExpectedRuntimeBandwidthHz = 250;
  ExpectedRitStepHz = 50;
  ExpectedUpperRitHz = 500;
  ExpectedFixedClickCount = 10;
  ExpectedClampedClickCount = 11;
  ExpectedRemotePitchHz = 360;
  ExpectedRemoteAmplitude = 18000;
  ExpectedRemoteMessage = 'TEST TEST';
  ExpectedTerminalOrdinal = 2048;
  ExpectedTerminalBits = '3f53fd06';
  ExpectedQskEnvelopeSamples = 50176;
  ExpectedRuntimeBandwidthFirstDivergence = 173;
  ExpectedFirstMonitorHash =
    '7d925cbba9a0bb2e86a48c5a1777c347cfed68080a559446d8e3ed3c9d6af4ee';
  ExpectedFullSecondMonitorHash =
    '98ed32d957fef5ee62e50a0a04cb063f4c027a9770d4a9e30b27c60dec52e234';
  ExpectedRuntimeBandwidthSecondHash =
    '7c95262971abcf6acf2f0324fd5b7ffc33c46032ba66111709f445f6a9bd8275';
  ExpectedFixedBandwidthProbeBits =
    '00000000,00000000,00000000,00000000,00000000,00000000,'
    + '00000000,af1ef2fe,bc1821a0,be84929e,bd5ab8c9,3e22613d';
  ExpectedFullSecondMonitorProbeBits =
    '3eb3ed67,3f007b40,3f1780a0,3f0e26cc,3eddd0a4,3e8492a0,'
    + '3f1c4001,3f0e26cc,3e8492a0,3eddd0a4,3e8492a0,3d5ab8db';
  ExpectedRuntimeBandwidthSecondProbeBits =
    '3eb3ed67,3f007b40,3f1780a0,3f0e26cc,3eddd0a4,3e8492a0,'
    + '3edcf889,379c5b52,3e80f105,3eddd0a6,3e8492a0,3d5ab8d6';
  ExpectedRuntimeRitFirstDivergence = -1;
  ExpectedRitFirstBlockHash =
    '2ed89f5a0efa340a7546fed86add29ed234bf16925cec59d797c41ca9a217ccb';
  ExpectedFixedRitSecondHash =
    'd22adaeb4130e08c7d2e8adeb9e37779ddd22e0b1967f9b875e66624ead5ffc3';
  ExpectedRuntimeRitSecondHash =
    'd22adaeb4130e08c7d2e8adeb9e37779ddd22e0b1967f9b875e66624ead5ffc3';
  ExpectedRitFirstProbeBits =
    '00000000,00000000,00000000,00000000,00000000,00000000,'
    + '00000000,b850d0b5,3a14b64f,bdc640b0,bd0b0f0c,3d0cba45';
  ExpectedFixedRitSecondProbeBits =
    '3db633f1,3debccf4,3dda374c,3dbfa71d,3de2177b,3dc603b0,'
    + 'bd7ffa6b,bbb23967,be2bdf5c,bdbb3448,bc5fca32,3d886648';
  ExpectedRuntimeRitSecondProbeBits =
    '3db633f1,3debccf4,3dda374c,3dbfa71d,3de2177b,3dc603b0,'
    + 'bd7ffa6b,bbb23967,be2bdf5c,bdbb3448,bc5fca32,3d886648';

  ExpectedProbeSampleIndexes:
    array[0..ExpectedProbeCount - 1] of Integer = (
      0,
      1,
      2,
      148,
      149,
      150,
      255,
      310,
      384,
      509,
      510,
      511
    );

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;
  TIntegerArray = array of Integer;
  TSingleArrayPair = array[0..1] of TSingleArray;
  TSingleSequence = array[0..9] of Single;

  TNoiseRunObservation = record
    Block: TSingleArray;
    TerminalRandom: Single;
    StationCount: Integer;
    PickStationCalls: Integer;
    GetCallCalls: Integer;
    StationClass: string;
    StationState: string;
    R1: Single;
    MyCall: string;
    HisCall: string;
    Amplitude: Single;
    Pitch: Integer;
    WpmS: Integer;
    WpmC: Integer;
    MessageSet: string;
    MessageText: string;
    EnvelopeSamples: Integer;
    SendPosition: Integer;
  end;

  TConstructorReplay = record
    Sequence: TSingleSequence;
    Patience: Integer;
    CallIndex: Integer;
    Amplitude: Single;
    Pitch: Integer;
    Wpm: Integer;
    MessageChoice: Integer;
    TerminalRandom: Single;
  end;

  TMonitorRunObservation = record
    Blocks: TSingleArrayPair;
    MessageText: string;
    EnvelopeSamples: Integer;
    SendPosition: Integer;
    TerminalRandom: Single;
  end;

  TOracleNoGuiWidgetSet = class(TNoGUIWidgetSet)
  public
    function EnumDisplayMonitors(
      Hdc: HDC;
      ClipRect: PRect;
      EnumProc: MonitorEnumProc;
      Data: LPARAM): LongBool; override;
    function GetDpiForMonitor(
      Monitor: HMONITOR;
      DpiType: TMonitorDpiType;
      out DpiX: UINT;
      out DpiY: UINT): HRESULT; override;
    function GetMonitorInfo(
      Monitor: HMONITOR;
      Info: PMonitorInfo): Boolean; override;
    function MonitorFromPoint(
      Point: TPoint;
      Flags: DWord): HMONITOR; override;
    function MonitorFromRect(
      Rect: PRect;
      Flags: DWord): HMONITOR; override;
    function MonitorFromWindow(
      Window: HWND;
      Flags: DWord): HMONITOR; override;
  end;

  TPositiveQrmContest = class(TContest)
  public
    function LoadCallHistory(
      const AUserCallsign: string): Boolean; override;
    function PickStation: Integer; override;
    procedure DropStation(Id: Integer); override;
    function GetCall(Id: Integer): string; override;
    procedure GetExchange(
      Id: Integer;
      out AStation: TDxStation); override;
  end;

  TStationAccess = class(TStation)
  public
    function ReadSendPosition: Integer;
  end;

  TScriptedStation = class(TStation)
  public
    constructor CreateScripted(
      ACollection: TCollection;
      const APitchHz: Integer;
      const AAmplitude: Single;
      const AMessage: string);
    procedure ProcessEvent(AEvent: TStationEvent); override;
  end;

var
  CallCatalog: TStringList;
  CurrentGetCallCalls: Integer;
  CurrentPickStationCalls: Integer;

function TOracleNoGuiWidgetSet.EnumDisplayMonitors(
  Hdc: HDC;
  ClipRect: PRect;
  EnumProc: MonitorEnumProc;
  Data: LPARAM): LongBool;
var
  Bounds: TRect;
begin
  Bounds := Rect(0, 0, 1920, 1080);
  Result := EnumProc(1, 0, @Bounds, Data);
end;

function TOracleNoGuiWidgetSet.GetDpiForMonitor(
  Monitor: HMONITOR;
  DpiType: TMonitorDpiType;
  out DpiX: UINT;
  out DpiY: UINT): HRESULT;
begin
  DpiX := 96;
  DpiY := 96;
  Result := S_OK;
end;

function TOracleNoGuiWidgetSet.GetMonitorInfo(
  Monitor: HMONITOR;
  Info: PMonitorInfo): Boolean;
begin
  if Info = nil then
    Exit(False);
  Info^.rcMonitor := Rect(0, 0, 1920, 1080);
  Info^.rcWork := Info^.rcMonitor;
  Info^.dwFlags := MONITORINFOF_PRIMARY;
  Result := True;
end;

function TOracleNoGuiWidgetSet.MonitorFromPoint(
  Point: TPoint;
  Flags: DWord): HMONITOR;
begin
  Result := 1;
end;

function TOracleNoGuiWidgetSet.MonitorFromRect(
  Rect: PRect;
  Flags: DWord): HMONITOR;
begin
  Result := 1;
end;

function TOracleNoGuiWidgetSet.MonitorFromWindow(
  Window: HWND;
  Flags: DWord): HMONITOR;
begin
  Result := 1;
end;

procedure RegisterNoguiHandlelessClasses;
begin
  if FindWSRegistered(TControl) = nil then
    RegisterWSComponent(TControl, TWSControl);
  if FindWSRegistered(TGraphicControl) = nil then
    RegisterWSComponent(TGraphicControl, TWSGraphicControl);
  if FindWSRegistered(TWinControl) = nil then
    RegisterWSComponent(TWinControl, TWSWinControl);
  if FindWSRegistered(TCustomControl) = nil then
    RegisterWSComponent(TCustomControl, TWSCustomControl);
  if FindWSRegistered(TScrollingWinControl) = nil then
    RegisterWSComponent(
      TScrollingWinControl,
      TWSScrollingWinControl);
  if FindWSRegistered(TCustomForm) = nil then
    RegisterWSComponent(TCustomForm, TWSCustomForm);
  if FindWSRegistered(TCustomPanel) = nil then
    RegisterWSComponent(TCustomPanel, TWSCustomPanel);
  if FindWSRegistered(TCustomComboBox) = nil then
    RegisterWSComponent(TCustomComboBox, TWSCustomComboBox);
  if FindWSRegistered(TMenuItem) = nil then
    RegisterWSComponent(TMenuItem, TWSMenuItem);
end;

function JsonEscape(const Value: string): string;
begin
  Result := StringReplace(Value, '\', '\\', [rfReplaceAll]);
  Result := StringReplace(Result, '"', '\"', [rfReplaceAll]);
  Result := StringReplace(Result, #13, '\r', [rfReplaceAll]);
  Result := StringReplace(Result, #10, '\n', [rfReplaceAll]);
end;

function FloatValue(const Value: Double): string;
var
  Normalized: Double;
begin
  if Value = 0 then
    Normalized := 0
  else
    Normalized := Value;
  Result := FloatToStrF(Normalized, ffFixed, 18, 9);
end;

function IsSha256(const Value: string): Boolean;
var
  Index: Integer;
begin
  Result := Length(Value) = 64;
  if not Result then
    Exit;
  for Index := 1 to Length(Value) do
    if not (Value[Index] in ['0'..'9', 'a'..'f']) then
      Exit(False);
end;

function RotateRight32(
  const Value: LongWord;
  const Count: Byte): LongWord;
begin
  Result := (Value shr Count) or (Value shl (32 - Count));
end;

function Sha256Bytes(const Value: RawByteString): string;
const
  RoundConstants: array[0..63] of LongWord = (
    $428A2F98, $71374491, $B5C0FBCF, $E9B5DBA5,
    $3956C25B, $59F111F1, $923F82A4, $AB1C5ED5,
    $D807AA98, $12835B01, $243185BE, $550C7DC3,
    $72BE5D74, $80DEB1FE, $9BDC06A7, $C19BF174,
    $E49B69C1, $EFBE4786, $0FC19DC6, $240CA1CC,
    $2DE92C6F, $4A7484AA, $5CB0A9DC, $76F988DA,
    $983E5152, $A831C66D, $B00327C8, $BF597FC7,
    $C6E00BF3, $D5A79147, $06CA6351, $14292967,
    $27B70A85, $2E1B2138, $4D2C6DFC, $53380D13,
    $650A7354, $766A0ABB, $81C2C92E, $92722C85,
    $A2BFE8A1, $A81A664B, $C24B8B70, $C76C51A3,
    $D192E819, $D6990624, $F40E3585, $106AA070,
    $19A4C116, $1E376C08, $2748774C, $34B0BCB5,
    $391C0CB3, $4ED8AA4A, $5B9CCA4F, $682E6FF3,
    $748F82EE, $78A5636F, $84C87814, $8CC70208,
    $90BEFFFA, $A4506CEB, $BEF9A3F7, $C67178F2
  );
var
  A: LongWord;
  B: LongWord;
  C: LongWord;
  Ch: LongWord;
  D: LongWord;
  E: LongWord;
  F: LongWord;
  G: LongWord;
  H: LongWord;
  Index: Integer;
  Major: LongWord;
  MessageBytes: TByteBuffer;
  MessageLength: Integer;
  Offset: Integer;
  PaddedLength: Integer;
  Schedule: TSha256Schedule;
  Sigma0: LongWord;
  Sigma1: LongWord;
  State: TSha256State;
  Temp1: LongWord;
  Temp2: LongWord;
  ValueBitLength: QWord;
begin
  State[0] := $6A09E667;
  State[1] := $BB67AE85;
  State[2] := $3C6EF372;
  State[3] := $A54FF53A;
  State[4] := $510E527F;
  State[5] := $9B05688C;
  State[6] := $1F83D9AB;
  State[7] := $5BE0CD19;

  MessageLength := Length(Value);
  PaddedLength := ((MessageLength + 9 + 63) div 64) * 64;
  SetLength(MessageBytes, PaddedLength);
  for Index := 0 to MessageLength - 1 do
    MessageBytes[Index] := Byte(Value[Index + 1]);
  MessageBytes[MessageLength] := $80;
  ValueBitLength := QWord(MessageLength) * 8;
  for Index := 0 to 7 do
    MessageBytes[PaddedLength - 1 - Index] :=
      Byte(ValueBitLength shr (Index * 8));

  Offset := 0;
  while Offset < PaddedLength do
  begin
    for Index := 0 to 15 do
      Schedule[Index] :=
        (LongWord(MessageBytes[Offset + Index * 4]) shl 24)
        or (LongWord(MessageBytes[Offset + Index * 4 + 1]) shl 16)
        or (LongWord(MessageBytes[Offset + Index * 4 + 2]) shl 8)
        or LongWord(MessageBytes[Offset + Index * 4 + 3]);
    for Index := 16 to 63 do
    begin
      Sigma0 :=
        RotateRight32(Schedule[Index - 15], 7)
        xor RotateRight32(Schedule[Index - 15], 18)
        xor (Schedule[Index - 15] shr 3);
      Sigma1 :=
        RotateRight32(Schedule[Index - 2], 17)
        xor RotateRight32(Schedule[Index - 2], 19)
        xor (Schedule[Index - 2] shr 10);
      Schedule[Index] :=
        Schedule[Index - 16]
        + Sigma0
        + Schedule[Index - 7]
        + Sigma1;
    end;

    A := State[0];
    B := State[1];
    C := State[2];
    D := State[3];
    E := State[4];
    F := State[5];
    G := State[6];
    H := State[7];
    for Index := 0 to 63 do
    begin
      Sigma1 :=
        RotateRight32(E, 6)
        xor RotateRight32(E, 11)
        xor RotateRight32(E, 25);
      Ch := (E and F) xor ((not E) and G);
      Temp1 :=
        H + Sigma1 + Ch + RoundConstants[Index] + Schedule[Index];
      Sigma0 :=
        RotateRight32(A, 2)
        xor RotateRight32(A, 13)
        xor RotateRight32(A, 22);
      Major := (A and B) xor (A and C) xor (B and C);
      Temp2 := Sigma0 + Major;
      H := G;
      G := F;
      F := E;
      E := D + Temp1;
      D := C;
      C := B;
      B := A;
      A := Temp1 + Temp2;
    end;

    State[0] := State[0] + A;
    State[1] := State[1] + B;
    State[2] := State[2] + C;
    State[3] := State[3] + D;
    State[4] := State[4] + E;
    State[5] := State[5] + F;
    State[6] := State[6] + G;
    State[7] := State[7] + H;
    Inc(Offset, 64);
  end;

  Result := '';
  for Index := 0 to 7 do
    Result := Result + LowerCase(IntToHex(State[Index], 8));
end;

procedure ValidateSha256Implementation;
begin
  if Sha256Bytes('') <>
      'e3b0c44298fc1c149afbf4c8996fb924'
      + '27ae41e4649b934ca495991b7852b855' then
    raise Exception.Create('SHA-256 empty-vector self-test failed');
  if Sha256Bytes('abc') <>
      'ba7816bf8f01cfea414140de5dae2223'
      + 'b00361a396177a9cb410ff61f20015ad' then
    raise Exception.Create('SHA-256 abc-vector self-test failed');
end;

function FloatBlockSha256(const Data: TSingleArray): string;
var
  Raw: RawByteString;
begin
  SetLength(Raw, Length(Data) * SizeOf(Single));
  if Length(Raw) > 0 then
    Move(Data[0], Raw[1], Length(Raw));
  Result := Sha256Bytes(Raw);
end;

function SingleBits(const Value: Single): string;
var
  Bits: LongWord;
begin
  if SizeOf(Value) <> SizeOf(Bits) then
    raise Exception.Create('CE Single storage is not 32-bit');
  Move(Value, Bits, SizeOf(Bits));
  Result := LowerCase(IntToHex(Bits, 8));
end;

procedure ValidateSingleStorage;
var
  Value: Single;
begin
  Value := 1;
  if SingleBits(Value) <> '3f800000' then
    raise Exception.Create(
      'CE Single storage is not little-endian IEEE-754 binary32');
end;

procedure RequireExactObjectFields(
  const Value: TJSONObject;
  const ExpectedNames: array of string);
var
  ExpectedIndex: Integer;
  Found: Boolean;
  Index: Integer;
begin
  if Value.Count <> Length(ExpectedNames) then
    raise Exception.Create('scenario input has unsupported fields');
  for Index := 0 to Value.Count - 1 do
  begin
    Found := False;
    for ExpectedIndex := Low(ExpectedNames) to High(ExpectedNames) do
      if Value.Names[Index] = ExpectedNames[ExpectedIndex] then
      begin
        Found := True;
        Break;
      end;
    if not Found then
      raise Exception.Create(
        'scenario input has unsupported field: ' + Value.Names[Index]);
  end;
end;

function RequireInteger(
  const Input: TJSONObject;
  const Name: string;
  const Minimum: Integer;
  const Maximum: Integer): Integer;
var
  Data: TJSONData;
  Value: Int64;
begin
  Data := Input.Find(Name);
  if (Data = nil) or not (Data is TJSONIntegerNumber) then
    raise Exception.Create(Name + ' is not an integer');
  Value := Data.AsInt64;
  if (Value < Minimum) or (Value > Maximum) then
    raise Exception.Create(Name + ' is outside its supported range');
  Result := Integer(Value);
end;

function RequireString(
  const Input: TJSONObject;
  const Name: string): string;
var
  Data: TJSONData;
begin
  Data := Input.Find(Name);
  if (Data = nil) or (Data.JSONType <> jtString) then
    raise Exception.Create(Name + ' is not a string');
  Result := Data.AsString;
  if Result = '' then
    raise Exception.Create(Name + ' is empty');
end;

function RequireIntegerArray(
  const Input: TJSONObject;
  const Name: string;
  const Minimum: Integer;
  const Maximum: Integer): TIntegerArray;
var
  ArrayData: TJSONArray;
  Data: TJSONData;
  Index: Integer;
  Value: Int64;
begin
  Data := Input.Find(Name);
  if (Data = nil) or (Data.JSONType <> jtArray) then
    raise Exception.Create(Name + ' is not an array');
  ArrayData := TJSONArray(Data);
  SetLength(Result, ArrayData.Count);
  for Index := 0 to ArrayData.Count - 1 do
  begin
    Data := ArrayData.Items[Index];
    if not (Data is TJSONIntegerNumber) then
      raise Exception.Create(Name + ' contains a non-integer');
    Value := Data.AsInt64;
    if (Value < Minimum) or (Value > Maximum) then
      raise Exception.Create(Name + ' contains an out-of-range value');
    Result[Index] := Integer(Value);
  end;
end;

function LoadScenarioInput(
  const InputPath: string;
  const Scenario: string;
  const InputSha256: string): TJSONObject;
var
  Data: TJSONData;
  FileStream: TFileStream;
  RawInput: RawByteString;
  ScenarioData: TJSONData;
begin
  if ExtractFileName(InputPath) <> InputSha256 + '.json' then
    raise Exception.Create('scenario input path is not content addressed');
  if not FileExists(InputPath) then
    raise Exception.Create('scenario input file does not exist');
  FileStream := TFileStream.Create(
    InputPath,
    fmOpenRead or fmShareDenyWrite);
  try
    if FileStream.Size > High(Integer) then
      raise Exception.Create('scenario input file is too large');
    SetLength(RawInput, Integer(FileStream.Size));
    if Length(RawInput) > 0 then
      FileStream.ReadBuffer(RawInput[1], Length(RawInput));
    if Sha256Bytes(RawInput) <> InputSha256 then
      raise Exception.Create('scenario input SHA-256 mismatch');
    FileStream.Position := 0;
    Data := GetJSON(FileStream, True);
  finally
    FileStream.Free;
  end;
  if not (Data is TJSONObject) then
  begin
    Data.Free;
    raise Exception.Create('scenario input root is not an object');
  end;
  Result := TJSONObject(Data);
  try
    ScenarioData := Result.Find('scenario');
    if (ScenarioData = nil)
      or (ScenarioData.JSONType <> jtString)
      or (ScenarioData.AsString <> Scenario) then
      raise Exception.Create('scenario input discriminator mismatch');
  except
    Result.Free;
    raise;
  end;
end;

procedure Emit(
  const Scenario: string;
  const AdapterId: string;
  const VersionId: string;
  const Source: string;
  const SourceSha256: string;
  const BuildRecipe: string;
  const BuildRecipeSha256: string;
  const CaseDefinitionSha256: string;
  const InputSha256: string;
  const Values: TStrings);
var
  Index: Integer;
begin
  Write(
    '{"scenario":"', JsonEscape(Scenario),
    '","adapterId":"', JsonEscape(AdapterId),
    '","versionId":"', JsonEscape(VersionId),
    '","source":"', JsonEscape(Source),
    '","sourceSha256":"', JsonEscape(SourceSha256),
    '","buildRecipe":"', JsonEscape(BuildRecipe),
    '","buildRecipeSha256":"', JsonEscape(BuildRecipeSha256),
    '","caseDefinitionSha256":"', JsonEscape(CaseDefinitionSha256),
    '","inputSha256":"', JsonEscape(InputSha256),
    '","values":[');
  for Index := 0 to Values.Count - 1 do
  begin
    if Index > 0 then
      Write(',');
    Write('"', JsonEscape(Values[Index]), '"');
  end;
  WriteLn(']}');
end;

procedure FailIfCalled(const Name: string);
begin
  raise Exception.Create('fail-if-called contest stub: ' + Name);
end;

function TPositiveQrmContest.LoadCallHistory(
  const AUserCallsign: string): Boolean;
begin
  FailIfCalled('LoadCallHistory');
  Result := False;
end;

function TPositiveQrmContest.PickStation: Integer;
begin
  Inc(CurrentPickStationCalls);
  if CurrentPickStationCalls <> 1 then
    raise Exception.Create('CE QRM called PickStation more than once');
  Result := -1;
end;

procedure TPositiveQrmContest.DropStation(Id: Integer);
begin
  FailIfCalled('DropStation');
end;

function TPositiveQrmContest.GetCall(Id: Integer): string;
var
  Index: Integer;
begin
  Inc(CurrentGetCallCalls);
  if CurrentGetCallCalls <> 1 then
    raise Exception.Create('CE QRM called GetCall more than once');
  if Id <> -1 then
    raise Exception.Create('CE QRM PickCallOnly changed its station ID');
  Index := Random(CallCatalog.Count);
  if Index <> ExpectedCatalogIndex then
    raise Exception.Create(
      'CE QRM call-catalog index changed: ' + IntToStr(Index));
  Result := CallCatalog[Index];
  if Result <> ExpectedCatalogCall then
    raise Exception.Create(
      'CE QRM selected callsign changed: ' + Result);
end;

procedure TPositiveQrmContest.GetExchange(
  Id: Integer;
  out AStation: TDxStation);
begin
  FailIfCalled('GetExchange');
  AStation := nil;
end;

function TStationAccess.ReadSendPosition: Integer;
begin
  Result := SendPos;
end;

constructor TScriptedStation.CreateScripted(
  ACollection: TCollection;
  const APitchHz: Integer;
  const AAmplitude: Single;
  const AMessage: string);
begin
  inherited CreateStation;
  Collection := ACollection;
  Pitch := APitchHz;
  Amplitude := AAmplitude;
  WpmS := Ini.Wpm;
  WpmC := WpmS;
  SendText(AMessage);
end;

procedure TScriptedStation.ProcessEvent(AEvent: TStationEvent);
begin
  raise Exception.Create(
    'fixed-station ProcessEvent was unexpectedly called');
end;

procedure LoadAndValidateCallCatalog(const LegacyRoot: string);
const
  CharacterCount = 37;
  IndexBytes = ((CharacterCount * CharacterCount) + 1) * SizeOf(Integer);
var
  CallValue: string;
  Data: TByteBuffer;
  FileStream: TFileStream;
  Index: Integer;
  MasterPath: string;
  Raw: RawByteString;
  Start: Integer;
begin
  MasterPath := LegacyRoot + 'MASTER.DTA';
  if not FileExists(MasterPath) then
    raise Exception.Create('pinned MASTER.DTA is missing');
  FileStream := TFileStream.Create(
    MasterPath,
    fmOpenRead or fmShareDenyWrite);
  try
    if FileStream.Size <> ExpectedMasterDataLength then
      raise Exception.Create('pinned MASTER.DTA length changed');
    SetLength(Raw, Integer(FileStream.Size));
    if Length(Raw) > 0 then
      FileStream.ReadBuffer(Raw[1], Length(Raw));
  finally
    FileStream.Free;
  end;
  if Sha256Bytes(Raw) <> ExpectedMasterDataSha256 then
    raise Exception.Create('pinned MASTER.DTA SHA-256 changed');

  SetLength(Data, Length(Raw));
  if Length(Raw) > 0 then
    Move(Raw[1], Data[0], Length(Raw));
  if (Length(Data) < IndexBytes)
    or (PInteger(@Data[0])^ <> IndexBytes)
    or (PInteger(@Data[IndexBytes - SizeOf(Integer)])^
        <> Length(Data)) then
    raise Exception.Create('pinned MASTER.DTA index is invalid');

  CallCatalog := TStringList.Create;
  CallCatalog.CaseSensitive := True;
  Start := IndexBytes;
  for Index := IndexBytes to Length(Data) do
    if (Index = Length(Data)) or (Data[Index] = 0) then
    begin
      if Index > Start then
      begin
        SetString(
          CallValue,
          PAnsiChar(@Data[Start]),
          Index - Start);
        if Copy(CallValue, 1, 4) <> 'VER2' then
          CallCatalog.Add(CallValue);
      end;
      Start := Index + 1;
    end;
  CallCatalog.Sort;
  for Index := CallCatalog.Count - 1 downto 1 do
    if CallCatalog[Index] = CallCatalog[Index - 1] then
      CallCatalog.Delete(Index);
  if CallCatalog.Count <> ExpectedCatalogCount then
    raise Exception.Create(
      'pinned MASTER.DTA call count changed: '
      + IntToStr(CallCatalog.Count));
  if CallCatalog[ExpectedCatalogIndex] <> ExpectedCatalogCall then
    raise Exception.Create(
      'pinned MASTER.DTA selected call changed');
end;

procedure ConfigureIni(const QrmEnabled: Boolean);
begin
  Ini.Call := 'W7SST';
  Ini.Wpm := 30;
  Ini.Bandwidth := ExpectedBandwidthHz;
  Ini.Pitch := ExpectedPitchHz;
  Ini.Qsk := False;
  Ini.Rit := 0;
  Ini.BufSize := ExpectedBlockSize;
  Ini.Activity := 1;
  Ini.Qrn := False;
  Ini.Qrm := QrmEnabled;
  Ini.Qsb := False;
  Ini.Flutter := False;
  Ini.Lids := False;
  Ini.Duration := 30;
  Ini.RunMode := rmStop;
  Ini.SaveWav := False;
  Ini.FarnsworthCharRate := Ini.Wpm;
  Ini.AllStationsWpmS := 0;
  Ini.CallsFromKeyer := False;
  Ini.SimContest := scWpx;
  Ini.ActiveContest := @Ini.ContestDefinitions[scWpx];
end;

procedure CreateRuntime(
  const Seed: Integer;
  const QrmEnabled: Boolean);
var
  BandwidthIndex: Integer;
begin
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime globals were not initially clear');

  CurrentGetCallCalls := 0;
  CurrentPickStationCalls := 0;
  ConfigureIni(QrmEnabled);
  RandSeed := Seed;
  gDXCCList := TDXCC.Create;
  MakeKeyer(ExpectedSampleRate, ExpectedBlockSize);
  Tst := TPositiveQrmContest.Create;
  MainForm := TMainForm.CreateNew(nil);
  MainForm.Panel2 := TPanel.Create(MainForm);
  MainForm.Panel4 := TPanel.Create(MainForm);
  MainForm.Panel7 := TPanel.Create(MainForm);
  MainForm.Panel8 := TPanel.Create(MainForm);
  MainForm.Panel8.Width := 100;
  MainForm.Shape2 := TShape.Create(MainForm);
  MainForm.SpinEdit1 := TSpinEdit.Create(MainForm);
  MainForm.SpinEdit1.MinValue := 10;
  MainForm.SpinEdit1.MaxValue := 120;
  MainForm.ComboBox2 := TComboBox.Create(MainForm);
  for BandwidthIndex := 0 to 10 do
    MainForm.ComboBox2.Items.Add(
      IntToStr(100 + BandwidthIndex * 50) + ' Hz');
  MainForm.VolumeSlider1 := TVolumeSlider.Create(MainForm);
  MainForm.VolumeSlider1.Parent := MainForm;
  MainForm.VolumeSlider1.Db := ExpectedMonitorLevelDb;
  MainForm.AlWavFile1 := TAlWavFile.Create(MainForm);
  MainForm.SetBw((ExpectedBandwidthHz - 100) div 50);

  if (Tst.ClassType <> TPositiveQrmContest)
    or (Tst.Me.ClassType <> TMyStation)
    or (Tst.Stations.Count <> 0)
    or (Tst.Me.State = stSending)
    or MainForm.HandleAllocated
    or MainForm.Panel2.HandleAllocated
    or MainForm.Panel4.HandleAllocated
    or MainForm.Panel7.HandleAllocated
    or MainForm.Panel8.HandleAllocated
    or MainForm.ComboBox2.HandleAllocated
    or MainForm.AlWavFile1.IsOpen
    or (Ini.Bandwidth <> ExpectedBandwidthHz)
    or Ini.Qrn
    or Ini.Qsb
    or Ini.Flutter
    or Ini.Lids
    or Ini.Qsk
    or (Round(MainForm.VolumeSlider1.Db) <> ExpectedMonitorLevelDb)
    or (Ini.Qrm <> QrmEnabled)
    or (MainForm.ComboBox2.ItemIndex <> 8)
    or (Tst.Filt.Points <> 15)
    or (Tst.Filt2.Points <> Tst.Filt.Points)
    or (CurrentPickStationCalls <> 0)
    or (CurrentGetCallCalls <> 0) then
    raise Exception.Create('CE positive-QRM runtime is not pristine');
end;

procedure DestroyRuntime;
begin
  if Assigned(Tst) then
    FreeAndNil(Tst);
  DestroyKeyer;
  if Assigned(gDXCCList) then
    FreeAndNil(gDXCCList);
  if Assigned(MainForm) then
    FreeAndNil(MainForm);
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime teardown was incomplete');
end;

function NormalizeSample(const Value: Single): Single;
begin
  Result := Value / 32768;
  if Result > 1 then
    Result := 1
  else if Result < -1 then
    Result := -1;
end;

function ObserveNoiseRun(
  const Seed: Integer;
  const QrmEnabled: Boolean): TNoiseRunObservation;
var
  BlockData: TSingleArray;
  SampleIndex: Integer;
  StartupIndex: Integer;
  Station: TStation;
begin
  CreateRuntime(Seed, QrmEnabled);
  try
    RandSeed := Seed;
    for StartupIndex := 0
        to ExpectedStartupRequestCount - 1 do
    begin
      BlockData := Tst.GetAudio;
      if (Length(BlockData) <> 1)
        or (BlockData[0] <> 0)
        or (Tst.BlockNumber <> StartupIndex + 1)
        or (Tst.Stations.Count <> 0)
        or (CurrentPickStationCalls <> 0)
        or (CurrentGetCallCalls <> 0) then
        raise Exception.Create(
          'CE positive-QRM startup framing changed');
    end;

    BlockData := Tst.GetAudio;
    if (Length(BlockData) <> ExpectedBlockSize)
      or (Tst.BlockNumber <> ExpectedStartupRequestCount + 1)
      or (Tst.Me.State = stSending)
      or MainForm.HandleAllocated
      or MainForm.Panel2.HandleAllocated
      or MainForm.Panel4.HandleAllocated
      or MainForm.Panel7.HandleAllocated
      or MainForm.Panel8.HandleAllocated
      or MainForm.ComboBox2.HandleAllocated
      or MainForm.AlWavFile1.IsOpen
      or (Ini.Bandwidth <> ExpectedBandwidthHz)
      or Ini.Qrn
      or Ini.Qsb
      or Ini.Flutter
      or Ini.Lids
      or Ini.Qsk
      or (Ini.Qrm <> QrmEnabled)
      or (MainForm.ComboBox2.ItemIndex <> 8)
      or (Tst.Filt.Points <> 15)
      or (Tst.Filt2.Points <> Tst.Filt.Points) then
      raise Exception.Create(
        'CE positive-QRM capture left its pinned receiver path');

    SetLength(Result.Block, ExpectedBlockSize);
    for SampleIndex := 0 to ExpectedBlockSize - 1 do
      Result.Block[SampleIndex] := NormalizeSample(BlockData[SampleIndex]);
    Result.StationCount := Tst.Stations.Count;
    Result.PickStationCalls := CurrentPickStationCalls;
    Result.GetCallCalls := CurrentGetCallCalls;

    if QrmEnabled then
    begin
      if Result.StationCount <> 1 then
        raise Exception.Create(
          'CE positive-QRM run did not retain one station');
      Station := Tst.Stations[0];
      if Station.ClassType <> TQrmStation then
        raise Exception.Create(
          'CE positive-QRM station has the wrong class');
      Result.StationClass := Station.ClassName;
      Result.StationState := ToStr(Station.State);
      Result.R1 := Station.R1;
      Result.MyCall := Station.MyCall;
      Result.HisCall := Station.HisCall;
      Result.Amplitude := Station.Amplitude;
      Result.Pitch := Station.Pitch;
      Result.WpmS := Station.WpmS;
      Result.WpmC := Station.WpmC;
      Result.MessageSet := ToStr(Station.Msg);
      Result.MessageText := Station.MsgText;
      Result.EnvelopeSamples := Length(Station.Envelope);
      Result.SendPosition :=
        TStationAccess(Station).ReadSendPosition;
    end
    else
    begin
      if (Result.StationCount <> 0)
        or (CurrentPickStationCalls <> 0)
        or (CurrentGetCallCalls <> 0) then
        raise Exception.Create(
          'CE clean QRM run unexpectedly created a station');
    end;
    Result.TerminalRandom := Random;
  finally
    DestroyRuntime;
  end;
end;

function SequenceBits(const Values: TSingleSequence): string;
var
  Index: Integer;
begin
  Result := '';
  for Index := 0 to High(Values) do
  begin
    if Index > 0 then
      Result := Result + ',';
    Result := Result + SingleBits(Values[Index]);
  end;
end;

procedure ReplayConstructor(
  const Seed: Integer;
  out Replay: TConstructorReplay);
var
  Discarded: Extended;
  Index: Integer;
begin
  RandSeed := Seed;
  for Index := 0 to ExpectedQrmTriggerOrdinal - 1 do
    Discarded := Random;
  for Index := 0 to High(Replay.Sequence) do
    Replay.Sequence[Index] := Random;

  RandSeed := Seed;
  for Index := 0 to ExpectedQrmTriggerOrdinal do
    Discarded := Random;
  Discarded := Random;
  Replay.Patience := 1 + Random(5);
  Replay.CallIndex := Random(ExpectedCatalogCount);
  Replay.Amplitude := 5000 + 25000 * Random;
  Replay.Pitch := Round(RndGaussLim(0, 300));
  Replay.Wpm := 30 + Random(20);
  Replay.MessageChoice := Random(7);
  Replay.TerminalRandom := Random;

  if (Discarded < 0)
    or (SequenceBits(Replay.Sequence)
        <> ExpectedConstructorSequenceBits)
    or (Replay.Sequence[0] >= 0.0002)
    or (SingleBits(Replay.Sequence[1]) <> ExpectedR1Bits)
    or (Replay.Patience <> ExpectedPatience)
    or (Replay.CallIndex <> ExpectedCatalogIndex)
    or (SingleBits(Replay.Amplitude) <> ExpectedAmplitudeBits)
    or (Replay.Pitch <> ExpectedPitchOffsetHz)
    or (Replay.Wpm <> ExpectedWordsPerMinute)
    or (Replay.MessageChoice <> ExpectedMessageChoice)
    or (SingleBits(Replay.TerminalRandom)
        <> ExpectedQrmTerminalBits) then
    raise Exception.Create(
      'CE positive-QRM constructor replay changed');
end;

function ProbeBits(
  const Block: TSingleArray;
  const ProbeIndexes: TIntegerArray): string;
var
  Index: Integer;
begin
  Result := '';
  for Index := 0 to High(ProbeIndexes) do
  begin
    if Index > 0 then
      Result := Result + ',';
    Result := Result + SingleBits(Block[ProbeIndexes[Index]]);
  end;
end;

function FirstDivergence(
  const Clean: TSingleArray;
  const Qrm: TSingleArray): Integer;
var
  Index: Integer;
begin
  if Length(Clean) <> Length(Qrm) then
    raise Exception.Create('CE QRM block lengths differ');
  for Index := 0 to High(Clean) do
    if SingleBits(Clean[Index]) <> SingleBits(Qrm[Index]) then
      Exit(Index);
  Result := -1;
end;

procedure AddBlockStatistics(
  const Prefix: string;
  const Block: TSingleArray;
  const ProbeIndexes: TIntegerArray;
  const Values: TStrings);
var
  Index: Integer;
  Peak: Double;
  SumSquares: Double;
begin
  Peak := 0;
  SumSquares := 0;
  for Index := 0 to High(Block) do
  begin
    Peak := Max(Peak, Abs(Block[Index]));
    SumSquares := SumSquares + Sqr(Block[Index]);
  end;
  Values.Add(
    Prefix
    + '|probe-bits=' + ProbeBits(Block, ProbeIndexes)
    + '|peak=' + FloatValue(Peak)
    + '|rms=' + FloatValue(Sqrt(SumSquares / Length(Block)))
    + '|float-sha256=' + FloatBlockSha256(Block));
end;

procedure ObservePositiveQrm(
  const Values: TStrings;
  const Input: TJSONObject);
var
  BandwidthHz: Integer;
  BlockSize: Integer;
  CallCatalogCount: Integer;
  Clean: TNoiseRunObservation;
  CleanHash: string;
  CleanProbeBits: string;
  CleanTerminalOrdinal: Integer;
  ComparedBlockCount: Integer;
  Divergence: Integer;
  MasterDataSha256: string;
  PitchHz: Integer;
  ProbeIndexes: TIntegerArray;
  ProbeText: string;
  Qrm: TNoiseRunObservation;
  QrmHash: string;
  QrmProbeBits: string;
  QrmTerminalOrdinal: Integer;
  QrmTriggerOrdinal: Integer;
  Replay: TConstructorReplay;
  SampleRate: Integer;
  Seed: Integer;
  SelectedCallIndex: Integer;
  StationCall: string;
  StartupRequestCount: Integer;
  Index: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'bandwidthHz',
      'blockSize',
      'callCatalogCount',
      'cleanTerminalRandomOrdinal',
      'comparedBlockCount',
      'masterDataSha256',
      'pitchHz',
      'probeSampleIndexes',
      'qrmTerminalRandomOrdinal',
      'qrmTriggerRandomOrdinal',
      'sampleRate',
      'scenario',
      'seed',
      'selectedCallIndex',
      'stationCall',
      'startupRequestCount'
    ]);
  SampleRate := RequireInteger(Input, 'sampleRate', 1, MaxInt);
  BlockSize := RequireInteger(Input, 'blockSize', 1, MaxInt);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  StartupRequestCount := RequireInteger(
    Input,
    'startupRequestCount',
    0,
    MaxInt);
  ComparedBlockCount := RequireInteger(
    Input,
    'comparedBlockCount',
    1,
    MaxInt);
  BandwidthHz := RequireInteger(Input, 'bandwidthHz', 1, MaxInt);
  PitchHz := RequireInteger(Input, 'pitchHz', 1, MaxInt);
  QrmTriggerOrdinal := RequireInteger(
    Input,
    'qrmTriggerRandomOrdinal',
    0,
    MaxInt);
  CleanTerminalOrdinal := RequireInteger(
    Input,
    'cleanTerminalRandomOrdinal',
    0,
    MaxInt);
  QrmTerminalOrdinal := RequireInteger(
    Input,
    'qrmTerminalRandomOrdinal',
    0,
    MaxInt);
  CallCatalogCount := RequireInteger(
    Input,
    'callCatalogCount',
    1,
    MaxInt);
  SelectedCallIndex := RequireInteger(
    Input,
    'selectedCallIndex',
    0,
    MaxInt);
  MasterDataSha256 := RequireString(Input, 'masterDataSha256');
  StationCall := RequireString(Input, 'stationCall');
  ProbeIndexes := RequireIntegerArray(
    Input,
    'probeSampleIndexes',
    0,
    ExpectedBlockSize - 1);

  if (SampleRate <> ExpectedSampleRate)
    or (BlockSize <> ExpectedBlockSize)
    or (Seed <> ExpectedSeed)
    or (StartupRequestCount <> ExpectedStartupRequestCount)
    or (ComparedBlockCount <> ExpectedComparedBlockCount)
    or (BandwidthHz <> ExpectedBandwidthHz)
    or (PitchHz <> ExpectedPitchHz)
    or (QrmTriggerOrdinal <> ExpectedQrmTriggerOrdinal)
    or (CleanTerminalOrdinal <> ExpectedCleanTerminalOrdinal)
    or (QrmTerminalOrdinal <> ExpectedQrmTerminalOrdinal)
    or (CallCatalogCount <> ExpectedCatalogCount)
    or (SelectedCallIndex <> ExpectedCatalogIndex)
    or (MasterDataSha256 <> ExpectedMasterDataSha256)
    or (StationCall <> 'W7SST')
    or (Length(ProbeIndexes) <> ExpectedProbeCount) then
    raise Exception.Create(
      'positive-QRM input does not match its pinned contract');
  for Index := 0 to ExpectedProbeCount - 1 do
    if ProbeIndexes[Index] <> ExpectedProbeSampleIndexes[Index] then
      raise Exception.Create(
        'positive-QRM probes do not match their pinned contract');

  ReplayConstructor(Seed, Replay);
  Clean := ObserveNoiseRun(Seed, False);
  Qrm := ObserveNoiseRun(Seed, True);

  if SingleBits(Clean.TerminalRandom) <> ExpectedCleanTerminalBits then
    raise Exception.Create('CE clean terminal random changed');
  if SingleBits(Qrm.TerminalRandom) <> ExpectedQrmTerminalBits then
    raise Exception.Create('CE QRM terminal random changed');
  if (Qrm.StationClass <> 'TQrmStation')
    or (Qrm.StationState <> 'stSending')
    or (SingleBits(Qrm.R1) <> ExpectedR1Bits)
    or (Qrm.MyCall <> ExpectedCatalogCall)
    or (Qrm.HisCall <> StationCall)
    or (SingleBits(Qrm.Amplitude) <> ExpectedAmplitudeBits)
    or (Qrm.Pitch <> ExpectedPitchOffsetHz)
    or (Qrm.WpmS <> ExpectedWordsPerMinute)
    or (Qrm.WpmC <> ExpectedWordsPerMinute)
    or (Qrm.MessageSet <> ExpectedMessageSet)
    or (Qrm.MessageText <> ExpectedMessageText)
    or (Qrm.EnvelopeSamples <> ExpectedEnvelopeSamples)
    or (Qrm.EnvelopeSamples div ExpectedBlockSize
        <> ExpectedEnvelopeBlocks)
    or (Qrm.SendPosition <> ExpectedSendPosition)
    or (Qrm.PickStationCalls <> 1)
    or (Qrm.GetCallCalls <> 1) then
    raise Exception.Create('CE positive-QRM station metadata changed');

  CleanHash := FloatBlockSha256(Clean.Block);
  QrmHash := FloatBlockSha256(Qrm.Block);
  CleanProbeBits := ProbeBits(Clean.Block, ProbeIndexes);
  QrmProbeBits := ProbeBits(Qrm.Block, ProbeIndexes);
  Divergence := FirstDivergence(Clean.Block, Qrm.Block);
  if Divergence <> ExpectedFirstDivergence then
    raise Exception.Create(
      'CE positive-QRM first divergence changed: '
      + IntToStr(Divergence));
  if CleanHash <> ExpectedCleanBlockSha256 then
    raise Exception.Create('CE clean block hash changed');
  if QrmHash <> ExpectedQrmBlockSha256 then
    raise Exception.Create('CE QRM block hash changed');
  if CleanProbeBits <> ExpectedCleanProbeBits then
    raise Exception.Create('CE clean block probes changed');
  if QrmProbeBits <> ExpectedQrmProbeBits then
    raise Exception.Create('CE QRM block probes changed');

  ProbeText := '';
  for Index := 0 to High(ProbeIndexes) do
  begin
    if Index > 0 then
      ProbeText := ProbeText + ',';
    ProbeText := ProbeText + IntToStr(ProbeIndexes[Index]);
  end;
  Values.Add(
    'contract=ce-live-qrm-first-trigger-v1'
    + '|fresh-runs=clean,qrm'
    + '|run-mode=rmStop'
    + '|seed=' + IntToStr(Seed)
    + '|sample-rate=' + IntToStr(SampleRate)
    + '|block-size=' + IntToStr(BlockSize)
    + '|startup-requests=' + IntToStr(StartupRequestCount)
    + '|absolute-block=6'
    + '|bandwidth-hz=' + IntToStr(BandwidthHz)
    + '|pitch-hz=' + IntToStr(PitchHz)
    + '|station-call=' + StationCall
    + '|qrn=false|qsb=false|flutter=false|qsk=false|lids=false');
  Values.Add(
    'catalog'
    + '|path=MASTER.DTA'
    + '|bytes=' + IntToStr(ExpectedMasterDataLength)
    + '|sha256=' + ExpectedMasterDataSha256
    + '|count=' + IntToStr(CallCatalog.Count)
    + '|selected-index=' + IntToStr(Replay.CallIndex)
    + '|selected-call=' + Qrm.MyCall);
  Values.Add(
    'random'
    + '|trigger-ordinal=' + IntToStr(ExpectedQrmTriggerOrdinal)
    + '|trigger-value=' + FloatValue(Replay.Sequence[0])
    + '|trigger-single-bits=' + SingleBits(Replay.Sequence[0])
    + '|draw-single-bits=' + SequenceBits(Replay.Sequence)
    + '|r1-ordinal=1025|r1-single-bits='
    + SingleBits(Replay.Sequence[1])
    + '|patience-ordinal=1026|patience='
    + IntToStr(Replay.Patience)
    + '|call-ordinal=1027|call-index='
    + IntToStr(Replay.CallIndex)
    + '|amplitude-ordinal=1028|amplitude='
    + FloatValue(Replay.Amplitude)
    + '|amplitude-single-bits=' + SingleBits(Replay.Amplitude)
    + '|gaussian-ordinals=1029,1030'
    + '|pitch-offset-hz=' + IntToStr(Replay.Pitch)
    + '|wpm-ordinal=1031|wpm=' + IntToStr(Replay.Wpm)
    + '|message-ordinal=1032|message-choice='
    + IntToStr(Replay.MessageChoice));
  Values.Add(
    'station'
    + '|count=' + IntToStr(Qrm.StationCount)
    + '|class=' + Qrm.StationClass
    + '|state=' + Qrm.StationState
    + '|my-call=' + Qrm.MyCall
    + '|his-call=' + Qrm.HisCall
    + '|r1-single-bits=' + SingleBits(Qrm.R1)
    + '|amplitude=' + FloatValue(Qrm.Amplitude)
    + '|amplitude-single-bits=' + SingleBits(Qrm.Amplitude)
    + '|pitch-offset-hz=' + IntToStr(Qrm.Pitch)
    + '|wpm-s=' + IntToStr(Qrm.WpmS)
    + '|wpm-c=' + IntToStr(Qrm.WpmC));
  Values.Add(
    'message'
    + '|set=' + Qrm.MessageSet
    + '|text=' + Qrm.MessageText
    + '|envelope-samples=' + IntToStr(Qrm.EnvelopeSamples)
    + '|envelope-blocks='
    + IntToStr(Qrm.EnvelopeSamples div ExpectedBlockSize)
    + '|send-position=' + IntToStr(Qrm.SendPosition)
    + '|remaining-blocks='
    + IntToStr(
        (Qrm.EnvelopeSamples - Qrm.SendPosition)
        div ExpectedBlockSize));
  Values.Add(
    'probes|sample-indexes=' + ProbeText);
  AddBlockStatistics('clean-block[0]', Clean.Block, ProbeIndexes, Values);
  AddBlockStatistics('qrm-block[0]', Qrm.Block, ProbeIndexes, Values);
  Values.Add(
    'comparison'
    + '|exact-equal=false'
    + '|first-divergence=' + IntToStr(Divergence)
    + '|clean-float-sha256=' + CleanHash
    + '|qrm-float-sha256=' + QrmHash
    + '|station-counts=0,1'
    + '|pick-station-calls=0,'
    + IntToStr(Qrm.PickStationCalls)
    + '|get-call-calls=0,' + IntToStr(Qrm.GetCallCalls));
  Values.Add(
    'terminal-random'
    + '|clean-ordinal=' + IntToStr(CleanTerminalOrdinal)
    + '|clean-value=' + FloatValue(Clean.TerminalRandom)
    + '|clean-single-bits=' + SingleBits(Clean.TerminalRandom)
    + '|qrm-ordinal=' + IntToStr(QrmTerminalOrdinal)
    + '|qrm-value=' + FloatValue(Qrm.TerminalRandom)
    + '|qrm-single-bits=' + SingleBits(Qrm.TerminalRandom));

  if Values.Count <> ExpectedValueCount then
    raise Exception.Create(
      'positive-QRM capture emitted an invalid row count');
end;

function ObserveBandwidthRun(
  const Seed: Integer;
  const RuntimeNarrowAfterFirstBlock: Boolean): TMonitorRunObservation;
var
  BlockIndex: Integer;
  BlockData: TSingleArray;
  SampleIndex: Integer;
  StartupIndex: Integer;
begin
  CreateRuntime(Seed, False);
  try
    Ini.Qsk := False;
    MainForm.VolumeSlider1.Db := ExpectedMonitorLevelDb;
    for StartupIndex := 0 to ExpectedStartupRequestCount - 1 do
    begin
      BlockData := Tst.GetAudio;
      if (Length(BlockData) <> 1)
        or (BlockData[0] <> 0)
        or (Tst.BlockNumber <> StartupIndex + 1)
        or (Tst.Stations.Count <> 0) then
        raise Exception.Create(
          'CE runtime-bandwidth startup framing changed');
    end;

    RandSeed := Seed;
    Tst.Me.SendMsg(msgCQ);
    if (Tst.Me.State <> stSending)
      or (Tst.Me.MsgText <> ExpectedLocalMessage)
      or (Length(Tst.Me.Envelope) = 0)
      or (TStationAccess(Tst.Me).ReadSendPosition <> 0) then
      raise Exception.Create('CE runtime-bandwidth local CQ did not start');

    Result.MessageText := Tst.Me.MsgText;
    Result.EnvelopeSamples := Length(Tst.Me.Envelope);
    for BlockIndex := 0 to 1 do
    begin
      if RuntimeNarrowAfterFirstBlock and (BlockIndex = 1) then
        MainForm.SetBw((ExpectedRuntimeBandwidthHz - 100) div 50);
      BlockData := Tst.GetAudio;
      if (Length(BlockData) <> ExpectedBlockSize)
        or (Tst.BlockNumber
            <> ExpectedStartupRequestCount + BlockIndex + 1)
        or (Tst.Me.State <> stSending)
        or (Tst.Stations.Count <> 0)
        or Ini.Qrn
        or Ini.Qrm
        or Ini.Qsb
        or Ini.Flutter
        or Ini.Lids
        or Ini.Qsk
        or (Ini.Bandwidth
            <> IfThen(
              RuntimeNarrowAfterFirstBlock and (BlockIndex = 1),
              ExpectedRuntimeBandwidthHz,
              ExpectedBandwidthHz))
        or (Round(MainForm.VolumeSlider1.Db)
            <> ExpectedMonitorLevelDb) then
        raise Exception.Create(
          'CE runtime-bandwidth capture left its pinned path');

      SetLength(Result.Blocks[BlockIndex], ExpectedBlockSize);
      for SampleIndex := 0 to ExpectedBlockSize - 1 do
        Result.Blocks[BlockIndex][SampleIndex] :=
          NormalizeSample(BlockData[SampleIndex]);
    end;

    Result.SendPosition := TStationAccess(Tst.Me).ReadSendPosition;
    Result.TerminalRandom := Random;
  finally
    DestroyRuntime;
  end;
end;

procedure ObserveRuntimeBandwidthChange(
  const Values: TStrings;
  const Input: TJSONObject);
var
  InitialBandwidthHz: Integer;
  BlockSize: Integer;
  Divergence: Integer;
  Index: Integer;
  MessageText: string;
  MonitorLevelDb: Integer;
  RuntimeBandwidthHz: Integer;
  PitchHz: Integer;
  ProbeIndexes: TIntegerArray;
  ProbeText: string;
  FixedBandwidth: TMonitorRunObservation;
  RuntimeNarrowBandwidth: TMonitorRunObservation;
  SampleRate: Integer;
  Seed: Integer;
  StationCall: string;
  StartupRequestCount: Integer;
  TerminalRandomOrdinal: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'blockSize',
      'initialBandwidthHz',
      'messageText',
      'monitorLevelDb',
      'pitchHz',
      'probeSampleIndexes',
      'runtimeBandwidthHz',
      'sampleRate',
      'scenario',
      'seed',
      'stationCall',
      'startupRequestCount',
      'terminalRandomOrdinal'
    ]);
  SampleRate := RequireInteger(Input, 'sampleRate', 1, MaxInt);
  BlockSize := RequireInteger(Input, 'blockSize', 1, MaxInt);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  StartupRequestCount := RequireInteger(
    Input,
    'startupRequestCount',
    0,
    MaxInt);
  TerminalRandomOrdinal := RequireInteger(
    Input,
    'terminalRandomOrdinal',
    1,
    MaxInt);
  InitialBandwidthHz := RequireInteger(
    Input,
    'initialBandwidthHz',
    100,
    1000);
  PitchHz := RequireInteger(Input, 'pitchHz', 1, MaxInt);
  MonitorLevelDb := RequireInteger(
    Input,
    'monitorLevelDb',
    -60,
    0);
  RuntimeBandwidthHz := RequireInteger(
    Input,
    'runtimeBandwidthHz',
    100,
    1000);
  StationCall := RequireString(Input, 'stationCall');
  MessageText := RequireString(Input, 'messageText');
  ProbeIndexes := RequireIntegerArray(
    Input,
    'probeSampleIndexes',
    0,
    ExpectedBlockSize - 1);

  if (SampleRate <> ExpectedSampleRate)
    or (BlockSize <> ExpectedBlockSize)
    or (Seed <> ExpectedSeed)
    or (StartupRequestCount <> ExpectedStartupRequestCount)
    or (TerminalRandomOrdinal <> ExpectedTerminalOrdinal)
    or (InitialBandwidthHz <> ExpectedBandwidthHz)
    or (PitchHz <> ExpectedPitchHz)
    or (MonitorLevelDb <> ExpectedMonitorLevelDb)
    or (RuntimeBandwidthHz <> ExpectedRuntimeBandwidthHz)
    or (StationCall <> ExpectedStationCall)
    or (MessageText <> ExpectedLocalMessage)
    or (Length(ProbeIndexes) <> ExpectedProbeCount) then
    raise Exception.Create(
      'runtime-bandwidth input does not match its pinned contract');
  for Index := 0 to ExpectedProbeCount - 1 do
    if ProbeIndexes[Index] <> ExpectedProbeSampleIndexes[Index] then
      raise Exception.Create(
        'runtime-bandwidth probes do not match their pinned contract');

  FixedBandwidth := ObserveBandwidthRun(Seed, False);
  RuntimeNarrowBandwidth := ObserveBandwidthRun(Seed, True);
  if (FixedBandwidth.MessageText <> ExpectedLocalMessage)
    or (RuntimeNarrowBandwidth.MessageText <> ExpectedLocalMessage)
    or (FixedBandwidth.EnvelopeSamples <> ExpectedQskEnvelopeSamples)
    or (FixedBandwidth.EnvelopeSamples
        <> RuntimeNarrowBandwidth.EnvelopeSamples)
    or (FixedBandwidth.SendPosition <> ExpectedBlockSize * 2)
    or (RuntimeNarrowBandwidth.SendPosition <> ExpectedBlockSize * 2)
    or (SingleBits(FixedBandwidth.TerminalRandom) <> ExpectedTerminalBits)
    or (SingleBits(RuntimeNarrowBandwidth.TerminalRandom)
        <> ExpectedTerminalBits) then
    raise Exception.Create(
      'CE runtime-bandwidth fixed observation changed');

  if not CompareMem(
      @FixedBandwidth.Blocks[0][0],
      @RuntimeNarrowBandwidth.Blocks[0][0],
      ExpectedBlockSize * SizeOf(Single)) then
    raise Exception.Create(
      'CE runtime-bandwidth first block changed before mutation');
  Divergence := FirstDivergence(
    FixedBandwidth.Blocks[1],
    RuntimeNarrowBandwidth.Blocks[1]);
  if (Divergence <> ExpectedRuntimeBandwidthFirstDivergence)
    or (FloatBlockSha256(FixedBandwidth.Blocks[0])
        <> ExpectedFirstMonitorHash)
    or (FloatBlockSha256(FixedBandwidth.Blocks[1])
        <> ExpectedFullSecondMonitorHash)
    or (FloatBlockSha256(RuntimeNarrowBandwidth.Blocks[1])
        <> ExpectedRuntimeBandwidthSecondHash)
    or (ProbeBits(FixedBandwidth.Blocks[0], ProbeIndexes)
        <> ExpectedFixedBandwidthProbeBits)
    or (ProbeBits(FixedBandwidth.Blocks[1], ProbeIndexes)
        <> ExpectedFullSecondMonitorProbeBits)
    or (ProbeBits(RuntimeNarrowBandwidth.Blocks[1], ProbeIndexes)
        <> ExpectedRuntimeBandwidthSecondProbeBits) then
    raise Exception.Create(
      'CE runtime-bandwidth fixed audio observation changed');
  ProbeText := '';
  for Index := 0 to High(ProbeIndexes) do
  begin
    if Index > 0 then
      ProbeText := ProbeText + ',';
    ProbeText := ProbeText + IntToStr(ProbeIndexes[Index]);
  end;

  Values.Add(
    'configuration'
    + '|run-mode=rmStop'
    + '|seed=' + IntToStr(Seed)
    + '|sample-rate=' + IntToStr(SampleRate)
    + '|block-size=' + IntToStr(BlockSize)
    + '|startup-requests=' + IntToStr(StartupRequestCount)
    + '|absolute-blocks=6,7'
    + '|bandwidth-change-hz=' + IntToStr(InitialBandwidthHz)
    + '-to-' + IntToStr(RuntimeBandwidthHz)
    + '|pitch-hz=' + IntToStr(PitchHz)
    + '|station-call=' + StationCall
    + '|monitor-level-db=' + IntToStr(MonitorLevelDb)
    + '|change-before-absolute-block=7'
    + '|filter-reset=true'
    + '|qrn=false|qrm=false|qsb=false|flutter=false|qsk=false|lids=false');
  Values.Add(
    'local-message'
    + '|text=' + FixedBandwidth.MessageText
    + '|rendered-local-samples=' + IntToStr(FixedBandwidth.SendPosition)
    + '|probe-sample-indexes=' + ProbeText);
  AddBlockStatistics(
    'bandwidth-before-change-block[0]',
    FixedBandwidth.Blocks[0],
    ProbeIndexes,
    Values);
  AddBlockStatistics(
    'bandwidth-fixed-500-block[1]',
    FixedBandwidth.Blocks[1],
    ProbeIndexes,
    Values);
  AddBlockStatistics(
    'bandwidth-runtime-250-block[1]',
    RuntimeNarrowBandwidth.Blocks[1],
    ProbeIndexes,
    Values);
  Values.Add(
    'comparison'
    + '|exact-equal=false'
    + '|first-divergence=' + IntToStr(Divergence)
    + '|bandwidth-fixed-500-float-sha256='
    + FloatBlockSha256(FixedBandwidth.Blocks[1])
    + '|bandwidth-runtime-250-float-sha256='
    + FloatBlockSha256(RuntimeNarrowBandwidth.Blocks[1]));
  Values.Add(
    'terminal-random'
    + '|ordinal=' + IntToStr(TerminalRandomOrdinal)
    + '|bandwidth-fixed-500-single-bits='
    + SingleBits(FixedBandwidth.TerminalRandom)
    + '|bandwidth-runtime-250-single-bits='
    + SingleBits(RuntimeNarrowBandwidth.TerminalRandom));

  if Values.Count <> ExpectedRuntimeBandwidthValueCount then
    raise Exception.Create(
      'runtime-bandwidth capture emitted an invalid row count');
end;

function ObserveRitRun(
  const Seed: Integer;
  const RitClickCount: Integer): TMonitorRunObservation;
var
  BlockIndex: Integer;
  BlockData: TSingleArray;
  ClickIndex: Integer;
  ExpectedRit: Integer;
  SampleIndex: Integer;
  StartupIndex: Integer;
begin
  CreateRuntime(Seed, False);
  try
    for StartupIndex := 0 to ExpectedStartupRequestCount - 1 do
    begin
      BlockData := Tst.GetAudio;
      if (Length(BlockData) <> 1)
        or (BlockData[0] <> 0)
        or (Tst.BlockNumber <> StartupIndex + 1)
        or (Tst.Stations.Count <> 0) then
        raise Exception.Create(
          'CE RIT upper-clamp startup framing changed');
    end;

    TScriptedStation.CreateScripted(
      Tst.Stations,
      ExpectedRemotePitchHz,
      ExpectedRemoteAmplitude,
      ExpectedRemoteMessage);
    RandSeed := Seed;
    if (Tst.Stations.Count <> 1)
      or (Ini.Rit <> 0) then
      raise Exception.Create(
        'CE RIT upper-clamp station did not start');

    for BlockIndex := 0 to 1 do
    begin
      ExpectedRit := 0;
      if BlockIndex = 1 then
      begin
        for ClickIndex := 0 to RitClickCount - 1 do
          MainForm.Panel8MouseDown(
            MainForm.Panel8,
            mbLeft,
            [],
            MainForm.Shape2.Left + MainForm.Shape2.Width + 1,
            0);
        ExpectedRit := Min(
          ExpectedUpperRitHz,
          RitClickCount * ExpectedRitStepHz);
        if Ini.Rit <> ExpectedRit then
          raise Exception.Create(
            'CE RIT upper-clamp handler reached an invalid value');
      end;

      BlockData := Tst.GetAudio;
      if (Length(BlockData) <> ExpectedBlockSize)
        or (Tst.BlockNumber
            <> ExpectedStartupRequestCount + BlockIndex + 1)
        or (Tst.Stations.Count <> 1)
        or Ini.Qrn
        or Ini.Qrm
        or Ini.Qsb
        or Ini.Flutter
        or Ini.Lids
        or Ini.Qsk
        or (Ini.Rit <> ExpectedRit) then
        raise Exception.Create(
          'CE RIT upper-clamp capture left its pinned path');

      SetLength(Result.Blocks[BlockIndex], ExpectedBlockSize);
      for SampleIndex := 0 to ExpectedBlockSize - 1 do
        Result.Blocks[BlockIndex][SampleIndex] :=
          NormalizeSample(BlockData[SampleIndex]);
    end;

    Result.SendPosition :=
      TStationAccess(Tst.Stations[0]).ReadSendPosition;
    Result.TerminalRandom := Random;
  finally
    DestroyRuntime;
  end;
end;

procedure ObserveRuntimeRitChange(
  const Values: TStrings;
  const Input: TJSONObject);
var
  BlockSize: Integer;
  Divergence: Integer;
  FixedRit: TMonitorRunObservation;
  Index: Integer;
  ProbeIndexes: TIntegerArray;
  ProbeText: string;
  RuntimeRit: TMonitorRunObservation;
  SampleRate: Integer;
  Seed: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'blockSize',
      'clampedClickCount',
      'fixedClickCount',
      'messageText',
      'probeSampleIndexes',
      'remoteAmplitude',
      'remotePitchHz',
      'ritStepHz',
      'sampleRate',
      'scenario',
      'seed',
      'startupRequestCount',
      'terminalRandomOrdinal',
      'upperBoundHz'
    ]);
  SampleRate := RequireInteger(Input, 'sampleRate', 1, MaxInt);
  BlockSize := RequireInteger(Input, 'blockSize', 1, MaxInt);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  if (SampleRate <> ExpectedSampleRate)
    or (BlockSize <> ExpectedBlockSize)
    or (Seed <> ExpectedSeed)
    or (RequireInteger(
          Input,
          'startupRequestCount',
          0,
          MaxInt) <> ExpectedStartupRequestCount)
    or (RequireInteger(
          Input,
          'terminalRandomOrdinal',
          1,
          MaxInt) <> ExpectedTerminalOrdinal)
    or (RequireInteger(
          Input,
          'ritStepHz',
          1,
          500) <> ExpectedRitStepHz)
    or (RequireInteger(
          Input,
          'upperBoundHz',
          1,
          2000) <> ExpectedUpperRitHz)
    or (RequireInteger(
          Input,
          'fixedClickCount',
          1,
          100) <> ExpectedFixedClickCount)
    or (RequireInteger(
          Input,
          'clampedClickCount',
          1,
          100) <> ExpectedClampedClickCount)
    or (RequireInteger(
          Input,
          'remotePitchHz',
          -1000,
          1000) <> ExpectedRemotePitchHz)
    or (RequireInteger(
          Input,
          'remoteAmplitude',
          1,
          100000) <> ExpectedRemoteAmplitude)
    or (RequireString(Input, 'messageText')
        <> ExpectedRemoteMessage) then
    raise Exception.Create(
      'RIT upper-clamp input does not match its pinned contract');

  ProbeIndexes := RequireIntegerArray(
    Input,
    'probeSampleIndexes',
    0,
    ExpectedBlockSize - 1);
  if Length(ProbeIndexes) <> ExpectedProbeCount then
    raise Exception.Create(
      'RIT upper-clamp probe count changed');
  ProbeText := '';
  for Index := 0 to ExpectedProbeCount - 1 do
  begin
    if ProbeIndexes[Index] <> ExpectedProbeSampleIndexes[Index] then
      raise Exception.Create(
        'RIT upper-clamp probes do not match their pinned contract');
    if Index > 0 then
      ProbeText := ProbeText + ',';
    ProbeText := ProbeText + IntToStr(ProbeIndexes[Index]);
  end;

  FixedRit := ObserveRitRun(Seed, ExpectedFixedClickCount);
  RuntimeRit := ObserveRitRun(Seed, ExpectedClampedClickCount);
  if not CompareMem(
      @FixedRit.Blocks[0][0],
      @RuntimeRit.Blocks[0][0],
      ExpectedBlockSize * SizeOf(Single)) then
    raise Exception.Create(
      'CE RIT upper-clamp first block changed before mutation');
  Divergence := FirstDivergence(
    FixedRit.Blocks[1],
    RuntimeRit.Blocks[1]);
  if (Divergence <> ExpectedRuntimeRitFirstDivergence)
    or (FixedRit.SendPosition <> ExpectedBlockSize * 2)
    or (RuntimeRit.SendPosition <> ExpectedBlockSize * 2)
    or (SingleBits(FixedRit.TerminalRandom) <> ExpectedTerminalBits)
    or (SingleBits(RuntimeRit.TerminalRandom)
        <> ExpectedTerminalBits)
    or (FloatBlockSha256(FixedRit.Blocks[0])
        <> ExpectedRitFirstBlockHash)
    or (FloatBlockSha256(RuntimeRit.Blocks[0])
        <> ExpectedRitFirstBlockHash)
    or (FloatBlockSha256(FixedRit.Blocks[1])
        <> ExpectedFixedRitSecondHash)
    or (FloatBlockSha256(RuntimeRit.Blocks[1])
        <> ExpectedRuntimeRitSecondHash)
    or (ProbeBits(FixedRit.Blocks[0], ProbeIndexes)
        <> ExpectedRitFirstProbeBits)
    or (ProbeBits(RuntimeRit.Blocks[0], ProbeIndexes)
        <> ExpectedRitFirstProbeBits)
    or (ProbeBits(FixedRit.Blocks[1], ProbeIndexes)
        <> ExpectedFixedRitSecondProbeBits)
    or (ProbeBits(RuntimeRit.Blocks[1], ProbeIndexes)
        <> ExpectedRuntimeRitSecondProbeBits) then
    raise Exception.Create(
      'CE RIT upper-clamp fixed observation changed');

  Values.Add(
    'configuration'
    + '|run-mode=rmStop'
    + '|seed=' + IntToStr(Seed)
    + '|sample-rate=' + IntToStr(SampleRate)
    + '|block-size=' + IntToStr(BlockSize)
    + '|startup-requests=' + IntToStr(ExpectedStartupRequestCount)
    + '|absolute-blocks=6,7'
    + '|rit-step-hz=' + IntToStr(ExpectedRitStepHz)
    + '|fixed-clicks=' + IntToStr(ExpectedFixedClickCount)
    + '|clamped-clicks=' + IntToStr(ExpectedClampedClickCount)
    + '|upper-bound-hz=' + IntToStr(ExpectedUpperRitHz)
    + '|fixed-result-hz=' + IntToStr(ExpectedUpperRitHz)
    + '|clamped-result-hz=' + IntToStr(ExpectedUpperRitHz)
    + '|change-before-absolute-block=7'
    + '|handler=TMainForm.Panel8MouseDown'
    + '|qrn=false|qrm=false|qsb=false|flutter=false|qsk=false|lids=false');
  Values.Add(
    'remote-station'
    + '|class=TScriptedStation'
    + '|pitch-offset-hz=' + IntToStr(ExpectedRemotePitchHz)
    + '|amplitude=' + IntToStr(ExpectedRemoteAmplitude)
    + '|message=' + ExpectedRemoteMessage
    + '|rendered-samples=' + IntToStr(FixedRit.SendPosition)
    + '|probe-sample-indexes=' + ProbeText);
  AddBlockStatistics(
    'rit-before-upper-bound-block[0]',
    FixedRit.Blocks[0],
    ProbeIndexes,
    Values);
  AddBlockStatistics(
    'rit-plus-500-block[1]',
    FixedRit.Blocks[1],
    ProbeIndexes,
    Values);
  AddBlockStatistics(
    'rit-extra-click-clamped-block[1]',
    RuntimeRit.Blocks[1],
    ProbeIndexes,
    Values);
  Values.Add(
    'comparison'
    + '|exact-equal=true'
    + '|first-divergence=' + IntToStr(Divergence)
    + '|rit-plus-500-float-sha256='
    + FloatBlockSha256(FixedRit.Blocks[1])
    + '|rit-extra-click-float-sha256='
    + FloatBlockSha256(RuntimeRit.Blocks[1]));
  Values.Add(
    'terminal-random'
    + '|ordinal=' + IntToStr(ExpectedTerminalOrdinal)
    + '|rit-plus-500-single-bits='
    + SingleBits(FixedRit.TerminalRandom)
    + '|rit-extra-click-single-bits='
    + SingleBits(RuntimeRit.TerminalRandom));

  if Values.Count <> ExpectedRuntimeRitValueCount then
    raise Exception.Create(
      'RIT upper-clamp capture emitted an invalid row count');
end;

procedure ObserveWpmUpperClamp(
  const Values: TStrings;
  const Input: TJSONObject);
var
  DefaultStepWpm: Integer;
  ExpectedClampWpm: Integer;
  FirstAfterWpm: Integer;
  InitialWpm: Integer;
  Key: Word;
  PageUpCount: Integer;
  RunModeId: string;
  Seed: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'defaultWpmStep',
      'expectedClampWpm',
      'initialWpm',
      'pageUpCount',
      'runModeId',
      'scenario',
      'seed'
    ]);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  DefaultStepWpm := RequireInteger(
    Input,
    'defaultWpmStep',
    1,
    20);
  ExpectedClampWpm := RequireInteger(
    Input,
    'expectedClampWpm',
    10,
    120);
  InitialWpm := RequireInteger(Input, 'initialWpm', 10, 120);
  PageUpCount := RequireInteger(Input, 'pageUpCount', 1, 10);
  RunModeId := RequireString(Input, 'runModeId');
  if (Seed <> ExpectedSeed)
    or (DefaultStepWpm <> 2)
    or (InitialWpm <> 118)
    or (ExpectedClampWpm <> 120)
    or (PageUpCount <> 2)
    or (RunModeId <> 'rmSingle') then
    raise Exception.Create(
      'WPM upper-clamp input does not match its pinned contract');

  CreateRuntime(Seed, False);
  try
    Ini.RunMode := rmSingle;
    MainForm.SetWpm(InitialWpm);
    if (Ini.Wpm <> InitialWpm)
      or (Ini.WpmStepRate <> DefaultStepWpm) then
      raise Exception.Create(
        'CE WPM upper-clamp runtime did not start at its contract');
    Values.Add(
      'configuration'
      + '|run-mode=' + RunModeId
      + '|seed=' + IntToStr(Seed)
      + '|initial-wpm=' + IntToStr(InitialWpm)
      + '|default-step-wpm=' + IntToStr(DefaultStepWpm)
      + '|page-up-count=' + IntToStr(PageUpCount)
      + '|handler=TMainForm.FormKeyDown');
    Values.Add('wpm-before|wpm=' + IntToStr(Ini.Wpm));
    Key := VK_PRIOR;
    MainForm.FormKeyDown(MainForm, Key, []);
    FirstAfterWpm := Ini.Wpm;
    if (Key <> 0) or (FirstAfterWpm <> ExpectedClampWpm) then
      raise Exception.Create(
        'CE first WPM PageUp did not reach the upper clamp');
    Values.Add(
      'wpm-after-first-page-up'
      + '|wpm=' + IntToStr(FirstAfterWpm)
      + '|delta-wpm=' + IntToStr(FirstAfterWpm - InitialWpm));
    Key := VK_PRIOR;
    MainForm.FormKeyDown(MainForm, Key, []);
    if (Key <> 0) or (Ini.Wpm <> ExpectedClampWpm) then
      raise Exception.Create(
        'CE extra WPM PageUp escaped the upper clamp');
    Values.Add(
      'wpm-after-extra-page-up'
      + '|wpm=' + IntToStr(Ini.Wpm)
      + '|delta-wpm=' + IntToStr(Ini.Wpm - InitialWpm));
    if Values.Count <> 4 then
      raise Exception.Create(
        'WPM upper-clamp capture emitted an invalid row count');
  finally
    DestroyRuntime;
  end;
end;

procedure ObserveDurationRange(
  const Values: TStrings;
  const Input: TJSONObject);
var
  ArbitraryValue: Integer;
  DurationControl: TSpinEdit;
  InitialValue: Integer;
  MaximumValue: Integer;
  MinimumValue: Integer;
  RequestAbove: Integer;
  RequestBelow: Integer;
  UpperValue: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'arbitraryValue',
      'initialValue',
      'maximumValue',
      'minimumValue',
      'requestAbove',
      'requestBelow',
      'scenario',
      'upperValue'
    ]);
  MinimumValue := RequireInteger(Input, 'minimumValue', 1, 240);
  MaximumValue := RequireInteger(Input, 'maximumValue', 1, 240);
  InitialValue := RequireInteger(Input, 'initialValue', 1, 240);
  ArbitraryValue := RequireInteger(Input, 'arbitraryValue', 1, 240);
  UpperValue := RequireInteger(Input, 'upperValue', 1, 240);
  RequestBelow := RequireInteger(Input, 'requestBelow', -1000, 0);
  RequestAbove := RequireInteger(Input, 'requestAbove', 241, 1000);
  if (MinimumValue <> 1)
    or (MaximumValue <> 240)
    or (InitialValue <> 30)
    or (ArbitraryValue <> 17)
    or (UpperValue <> 240)
    or (RequestBelow <> 0)
    or (RequestAbove <> 241) then
    raise Exception.Create(
      'duration range input does not match its pinned contract');

  DurationControl := TSpinEdit.Create(nil);
  try
    DurationControl.MinValue := MinimumValue;
    DurationControl.MaxValue := MaximumValue;
    DurationControl.Value := InitialValue;
    Values.Add(
      'duration-control|min=' + IntToStr(DurationControl.MinValue)
      + '|max=' + IntToStr(DurationControl.MaxValue)
      + '|initial=' + IntToStr(DurationControl.Value));
    DurationControl.Value := ArbitraryValue;
    Values.Add(
      'duration-arbitrary|request=' + IntToStr(ArbitraryValue)
      + '|result=' + IntToStr(DurationControl.Value));
    DurationControl.Value := UpperValue;
    Values.Add(
      'duration-upper|request=' + IntToStr(UpperValue)
      + '|result=' + IntToStr(DurationControl.Value));
    DurationControl.Value := RequestBelow;
    Values.Add(
      'duration-low-clamp|request=' + IntToStr(RequestBelow)
      + '|result=' + IntToStr(DurationControl.Value));
    DurationControl.Value := RequestAbove;
    Values.Add(
      'duration-high-clamp|request=' + IntToStr(RequestAbove)
      + '|result=' + IntToStr(DurationControl.Value));
    if Values.Count <> 5 then
      raise Exception.Create(
        'duration range capture emitted an invalid row count');
  finally
    DurationControl.Free;
  end;
end;

procedure ApplyPinnedCompetitionBranch(const Mode: TRunMode);
begin
  Ini.RunMode := Mode;
  if Mode = rmWpx then
  begin
    Ini.Qsb := True;
    Ini.Qrm := True;
    Ini.Qrn := True;
    Ini.Flutter := True;
    Ini.Lids := True;
    Ini.Duration := Ini.CompDuration;
  end
  else if Mode = rmHst then
  begin
    Ini.Qsb := False;
    Ini.Qrm := False;
    Ini.Qrn := False;
    Ini.Flutter := False;
    Ini.Lids := False;
    Ini.Duration := Ini.CompDuration;
    Ini.Activity := 4;
    Ini.Bandwidth := 600;
  end;
end;

procedure AddCompetitionObservation(
  const Values: TStrings;
  const ModeId: string);
begin
  Values.Add(
    'competition-settings'
    + '|mode=' + ModeId
    + '|duration-minutes=' + IntToStr(Ini.Duration)
    + '|activity=' + IntToStr(Ini.Activity)
    + '|bandwidth-hz=' + IntToStr(Ini.Bandwidth)
    + '|qsb=' + LowerCase(BoolToStr(Ini.Qsb, True))
    + '|qrm=' + LowerCase(BoolToStr(Ini.Qrm, True))
    + '|qrn=' + LowerCase(BoolToStr(Ini.Qrn, True))
    + '|flutter=' + LowerCase(BoolToStr(Ini.Flutter, True))
    + '|lids=' + LowerCase(BoolToStr(Ini.Lids, True)));
end;

procedure ObserveCompetitionForcedSettings(
  const Values: TStrings;
  const Input: TJSONObject);
var
  CompetitionDurationMinutes: Integer;
  InitialActivity: Integer;
  InitialBandwidthHz: Integer;
  InitialDurationMinutes: Integer;
  Seed: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'competitionDurationMinutes',
      'initialActivity',
      'initialBandwidthHz',
      'initialDurationMinutes',
      'scenario',
      'seed'
    ]);
  CompetitionDurationMinutes := RequireInteger(
    Input,
    'competitionDurationMinutes',
    1,
    60);
  InitialActivity := RequireInteger(Input, 'initialActivity', 1, 9);
  InitialBandwidthHz := RequireInteger(
    Input,
    'initialBandwidthHz',
    100,
    600);
  InitialDurationMinutes := RequireInteger(
    Input,
    'initialDurationMinutes',
    1,
    240);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  if (CompetitionDurationMinutes <> 17)
    or (InitialActivity <> 7)
    or (InitialBandwidthHz <> 500)
    or (InitialDurationMinutes <> 30)
    or (Seed <> 12345) then
    raise Exception.Create(
      'competition settings input does not match its pinned contract');

  Ini.CompDuration := CompetitionDurationMinutes;
  Ini.Duration := InitialDurationMinutes;
  Ini.Activity := InitialActivity;
  Ini.Bandwidth := InitialBandwidthHz;
  Ini.Qsb := False;
  Ini.Qrm := False;
  Ini.Qrn := False;
  Ini.Flutter := False;
  Ini.Lids := False;
  ApplyPinnedCompetitionBranch(rmWpx);
  AddCompetitionObservation(Values, 'rmWpx');

  Ini.Duration := InitialDurationMinutes;
  Ini.Activity := InitialActivity;
  Ini.Bandwidth := InitialBandwidthHz;
  Ini.Qsb := True;
  Ini.Qrm := True;
  Ini.Qrn := True;
  Ini.Flutter := True;
  Ini.Lids := True;
  ApplyPinnedCompetitionBranch(rmHst);
  AddCompetitionObservation(Values, 'rmHst');

  if Values.Count <> 2 then
    raise Exception.Create(
      'competition settings capture emitted an invalid row count');
end;

procedure RaiseIniError(const MessageText: string);
begin
  raise Exception.Create(MessageText);
end;

var
  CapturedIniErrors: TStrings = nil;

procedure CaptureIniError(const MessageText: string);
begin
  if not Assigned(CapturedIniErrors) then
    raise Exception.Create('INI error capture is not active');
  CapturedIniErrors.Add(
    Format(
      'error[%d]=%s',
      [CapturedIniErrors.Count, MessageText]));
end;

procedure CreateIniLoadForm;
var
  FrequencyHz: Integer;
  Mode: TRunMode;

  function NewMenuItem(const TagValue: Integer): TMenuItem;
  begin
    Result := TMenuItem.Create(MainForm);
    Result.Tag := TagValue;
  end;

begin
  MainForm := TMainForm.CreateNew(nil);
  MainForm.PopupMenu1 := TPopupMenu.Create(MainForm);
  for Mode := rmPileUp to rmHst do
    MainForm.PopupMenu1.Items.Add(NewMenuItem(Ord(Mode)));
  MainForm.ComboBox1 := TComboBox.Create(MainForm);
  for FrequencyHz := 300 to 900 do
    if FrequencyHz mod 50 = 0 then
      MainForm.ComboBox1.Items.Add(IntToStr(FrequencyHz) + ' Hz');
  MainForm.ComboBox2 := TComboBox.Create(MainForm);
  for FrequencyHz := 100 to 600 do
    if FrequencyHz mod 50 = 0 then
      MainForm.ComboBox2.Items.Add(IntToStr(FrequencyHz) + ' Hz');
  MainForm.SpinEdit2 := TSpinEdit.Create(MainForm);
  MainForm.SpinEdit3 := TSpinEdit.Create(MainForm);
  MainForm.CheckBox2 := TCheckBox.Create(MainForm);
  MainForm.CheckBox3 := TCheckBox.Create(MainForm);
  MainForm.CheckBox4 := TCheckBox.Create(MainForm);
  MainForm.CheckBox5 := TCheckBox.Create(MainForm);
  MainForm.CheckBox6 := TCheckBox.Create(MainForm);
  MainForm.VolumeSlider1 := TVolumeSlider.Create(MainForm);
  MainForm.mnuShowCallsignInfo := NewMenuItem(0);

  MainForm.CWMaxRxSpeedSet0 := NewMenuItem(0);
  MainForm.CWMaxRxSpeedSet1 := NewMenuItem(1);
  MainForm.CWMaxRxSpeedSet2 := NewMenuItem(2);
  MainForm.CWMaxRxSpeedSet4 := NewMenuItem(4);
  MainForm.CWMaxRxSpeedSet6 := NewMenuItem(6);
  MainForm.CWMaxRxSpeedSet8 := NewMenuItem(8);
  MainForm.CWMaxRxSpeedSet10 := NewMenuItem(10);
  MainForm.CWMinRxSpeedSet0 := NewMenuItem(0);
  MainForm.CWMinRxSpeedSet1 := NewMenuItem(1);
  MainForm.CWMinRxSpeedSet2 := NewMenuItem(2);
  MainForm.CWMinRxSpeedSet4 := NewMenuItem(4);
  MainForm.CWMinRxSpeedSet6 := NewMenuItem(6);
  MainForm.CWMinRxSpeedSet8 := NewMenuItem(8);
  MainForm.CWMinRxSpeedSet10 := NewMenuItem(10);

  MainForm.SerialNRSet1 := NewMenuItem(Ord(snStartContest));
  MainForm.SerialNRSet2 := NewMenuItem(Ord(snMidContest));
  MainForm.SerialNRSet3 := NewMenuItem(Ord(snEndContest));
  MainForm.SerialNRCustomRange := NewMenuItem(Ord(snCustomRange));
end;

procedure ObserveCleanProfileDefaults(
  const Values: TStrings;
  const Input: TJSONObject);
const
  SurfaceNames: array[0..2] of string = (
    'domain',
    'avalonia',
    'tui'
  );
var
  BandwidthHz: Integer;
  Index: Integer;
  IniPath: string;
  PitchHz: Integer;
  Row: string;
  Seed: Integer;
begin
  RequireExactObjectFields(Input, ['scenario', 'seed']);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  if Seed <> 12345 then
    raise Exception.Create(
      'clean-profile defaults input does not match its pinned contract');

  IniPath := ChangeFileExt(ParamStr(0), '.ini');
  if FileExists(IniPath) and not DeleteFile(IniPath) then
    raise Exception.Create('could not clear the clean-profile INI path');
  CreateIniLoadForm;
  try
    FromIni(RaiseIniError);
    PitchHz := 300 + MainForm.ComboBox1.ItemIndex * 50;
    BandwidthHz := 100 + MainForm.ComboBox2.ItemIndex * 50;
    if (Ini.Call <> 'VE3NEA')
      or (Ini.HamName <> '')
      or (Ini.Wpm <> 25)
      or (PitchHz <> 450)
      or (BandwidthHz <> 550)
      or (Ini.Activity <> 2)
      or (Ini.Duration <> 30)
      or (Ini.CompDuration <> 60)
      or (Ini.SimContest <> scWpx)
      or (Ini.DefaultRunMode <> rmPileUp)
      or (Ini.SerialNR <> snStartContest)
      or (Ini.MinRxWpm <> 0)
      or (Ini.MaxRxWpm <> 0)
      or (Ini.StationIdRate <> 3)
      or (Ini.MonLevel <> 0)
      or Ini.Qsk
      or Ini.Qsb
      or Ini.Qrm
      or Ini.Qrn
      or Ini.Flutter
      or Ini.Lids then
      raise Exception.CreateFmt(
        'CE clean-profile defaults changed: call=%s name=%s wpm=%d pitch=%d bandwidth=%d activity=%d duration=%d competition=%d contest=%d run-mode=%d serial=%d rx=%d/%d station-id=%d monitor=%d conditions=%s/%s/%s/%s/%s/%s',
        [Ini.Call, Ini.HamName, Ini.Wpm, PitchHz, BandwidthHz, Ini.Activity,
         Ini.Duration, Ini.CompDuration, Ord(Ini.SimContest),
         Ord(Ini.DefaultRunMode), Ord(Ini.SerialNR), Ini.MinRxWpm,
         Ini.MaxRxWpm, Ini.StationIdRate, Ini.MonLevel,
         BoolToStr(Ini.Qsk, True), BoolToStr(Ini.Qsb, True),
         BoolToStr(Ini.Qrm, True), BoolToStr(Ini.Qrn, True),
         BoolToStr(Ini.Flutter, True), BoolToStr(Ini.Lids, True)]);

    for Index := Low(SurfaceNames) to High(SurfaceNames) do
    begin
      Row :=
        'clean-profile-defaults'
        + '|surface=' + SurfaceNames[Index]
        + '|station-call=' + Ini.Call
        + '|hst-name=' + Ini.HamName
        + '|wpm=' + IntToStr(Ini.Wpm)
        + '|pitch-hz=' + IntToStr(PitchHz)
        + '|bandwidth-hz=' + IntToStr(BandwidthHz)
        + '|activity=' + IntToStr(Ini.Activity)
        + '|duration-minutes=' + IntToStr(Ini.Duration)
        + '|competition-duration-minutes=' + IntToStr(Ini.CompDuration)
        + '|contest=scWpx'
        + '|default-run-mode=rmPileup'
        + '|serial=snStartContest'
        + '|rx-below-wpm=' + IntToStr(Ini.MinRxWpm)
        + '|rx-above-wpm=' + IntToStr(Ini.MaxRxWpm)
        + '|station-id-rate=' + IntToStr(Ini.StationIdRate)
        + '|monitor-db=' + IntToStr(Ini.MonLevel)
        + '|qsk=' + LowerCase(BoolToStr(Ini.Qsk, True))
        + '|qsb=' + LowerCase(BoolToStr(Ini.Qsb, True))
        + '|qrm=' + LowerCase(BoolToStr(Ini.Qrm, True))
        + '|qrn=' + LowerCase(BoolToStr(Ini.Qrn, True))
        + '|flutter=' + LowerCase(BoolToStr(Ini.Flutter, True))
        + '|lids=' + LowerCase(BoolToStr(Ini.Lids, True));
      Values.Add(Row);
    end;
  finally
    FreeAndNil(MainForm);
    if FileExists(IniPath) and not DeleteFile(IniPath) then
      raise Exception.Create('could not remove the clean-profile INI path');
  end;

  if Values.Count <> 3 then
    raise Exception.Create(
      'clean-profile defaults capture emitted an invalid row count');
end;

procedure ObserveCeEncodingTranslation(
  const Values: TStrings;
  const Input: TJSONObject);
var
  BandwidthHz: Integer;
  IniFile: TIniFile;
  IniPath: string;
  PitchHz: Integer;
  Seed: Integer;
begin
  RequireExactObjectFields(Input, ['scenario', 'seed']);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  if Seed <> 12345 then
    raise Exception.Create(
      'CE encoding translation input does not match its pinned contract');

  IniPath := ChangeFileExt(ParamStr(0), '.ini');
  if FileExists(IniPath) and not DeleteFile(IniPath) then
    raise Exception.Create('could not clear the encoding-translation INI path');
  IniFile := TIniFile.Create(IniPath);
  try
    IniFile.WriteInteger(SEC_STN, 'Pitch', 6);
    IniFile.WriteInteger(SEC_STN, 'BandWidth', 4);
    IniFile.WriteInteger(SEC_TST, 'SimContest', 9);
    IniFile.WriteInteger(SEC_TST, 'DefaultRunMode', 3);
    IniFile.WriteInteger(SEC_STN, 'SerialNR', 2);
    IniFile.WriteInteger(SEC_SYS, 'BufSize', 4);
    IniFile.WriteInteger(SEC_TST, 'Duration', 47);
    IniFile.WriteInteger(SEC_TST, 'CompetitionDuration', 99);
    IniFile.WriteInteger(SEC_STN, 'SelfMonVolume', -99);
    IniFile.WriteInteger(SEC_SET, 'WpmStepRate', 0);
    IniFile.WriteInteger(SEC_SET, 'RitStepIncr', 700);
    IniFile.WriteInteger(SEC_SET, 'SingleCallStartDelay', 3000);
    IniFile.WriteString(SEC_STN, 'CqWpxExchange', '5NN 007');
    IniFile.WriteBool(SEC_STN, 'Qsk', True);
    IniFile.WriteBool(SEC_STN, 'SaveWav', True);
    IniFile.WriteBool(SEC_BND, 'Qsb', True);
    IniFile.WriteBool(SEC_BND, 'Qrm', False);
    IniFile.WriteBool(SEC_BND, 'Qrn', True);
    IniFile.WriteBool(SEC_BND, 'Flutter', False);
    IniFile.WriteBool(SEC_BND, 'Lids', True);
    IniFile.WriteBool(SEC_SYS, 'ShowCallsignInfo', False);
  finally
    IniFile.Free;
  end;

  CreateIniLoadForm;
  try
    FromIni(RaiseIniError);
    PitchHz := 300 + MainForm.ComboBox1.ItemIndex * 50;
    BandwidthHz := 100 + MainForm.ComboBox2.ItemIndex * 50;
    if (PitchHz <> 600)
      or (BandwidthHz <> 300)
      or (Ini.SimContest <> scAcag)
      or (Ini.DefaultRunMode <> rmWpx)
      or (Ini.SerialNR <> snEndContest)
      or (Ini.BufSize <> 1024)
      or (Ini.Duration <> 47)
      or (Ini.CompDuration <> 60)
      or (Ini.MonLevel <> -60)
      or (Ini.WpmStepRate <> 1)
      or (Ini.RitStepIncr <> 500)
      or (Ini.SingleCallStartDelay <> 2500)
      or (Ini.UserExchangeTbl[scWpx] <> '5NN 007')
      or not Ini.Qsk
      or not Ini.SaveWav
      or not Ini.Qsb
      or Ini.Qrm
      or not Ini.Qrn
      or Ini.Flutter
      or not Ini.Lids
      or MainForm.mnuShowCallsignInfo.Checked then
      raise Exception.Create('CE encoding translation behavior changed');

    Values.Add(
      'translation'
      + '|Station.Pitch=' + IntToStr(PitchHz)
      + '|Station.BandWidth=' + IntToStr(BandwidthHz)
      + '|Contest.SimContest=scAcag'
      + '|Contest.DefaultRunMode=rmWpx'
      + '|Station.SerialNR=' + IntToStr(Ord(Ini.SerialNR))
      + '|System.BufSize=' + IntToStr(Ini.BufSize)
      + '|Contest.Duration=' + IntToStr(Ini.Duration)
      + '|Contest.CompetitionDuration=' + IntToStr(Ini.CompDuration)
      + '|Station.SelfMonVolume=' + IntToStr(Ini.MonLevel)
      + '|Settings.WpmStepRate=' + IntToStr(Ini.WpmStepRate)
      + '|Settings.RitStepIncr=' + IntToStr(Ini.RitStepIncr)
      + '|Settings.SingleCallStartDelay='
      + IntToStr(Ini.SingleCallStartDelay)
      + '|Station.CqWpxExchange=' + Ini.UserExchangeTbl[scWpx]
      + '|Station.Qsk=' + BoolToStr(Ini.Qsk, True)
      + '|Station.SaveWav=' + BoolToStr(Ini.SaveWav, True)
      + '|Band.Qsb=' + BoolToStr(Ini.Qsb, True)
      + '|Band.Qrm=' + BoolToStr(Ini.Qrm, True)
      + '|Band.Qrn=' + BoolToStr(Ini.Qrn, True)
      + '|Band.Flutter=' + BoolToStr(Ini.Flutter, True)
      + '|Band.Lids=' + BoolToStr(Ini.Lids, True)
      + '|System.ShowCallsignInfo='
      + BoolToStr(MainForm.mnuShowCallsignInfo.Checked, True));
  finally
    FreeAndNil(MainForm);
    if FileExists(IniPath) and not DeleteFile(IniPath) then
      raise Exception.Create(
        'could not remove the encoding-translation INI path');
  end;

  if Values.Count <> 1 then
    raise Exception.Create(
      'CE encoding translation capture emitted an invalid row count');
end;

procedure ObserveProductionLegacyImport(
  const Values: TStrings;
  const Input: TJSONObject);
const
  SurfaceNames: array[0..1] of string = ('avalonia', 'tui');
var
  Index: Integer;
  IniFile: TIniFile;
  IniPath: string;
  PitchHz: Integer;
  Seed: Integer;
begin
  RequireExactObjectFields(Input, ['scenario', 'seed']);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  if Seed <> 12345 then
    raise Exception.Create(
      'production legacy import input does not match its pinned contract');

  IniPath := ChangeFileExt(ParamStr(0), '.ini');
  if FileExists(IniPath) and not DeleteFile(IniPath) then
    raise Exception.Create('could not clear the startup-import INI path');
  IniFile := TIniFile.Create(IniPath);
  try
    IniFile.WriteString(SEC_STN, 'Call', 'K7ABC');
    IniFile.WriteInteger(SEC_STN, 'Pitch', 6);
  finally
    IniFile.Free;
  end;

  CreateIniLoadForm;
  try
    FromIni(RaiseIniError);
    PitchHz := 300 + MainForm.ComboBox1.ItemIndex * 50;
    if (Ini.Call <> 'K7ABC') or (PitchHz <> 600) then
      raise Exception.Create('CE startup INI load behavior changed');
    for Index := Low(SurfaceNames) to High(SurfaceNames) do
      Values.Add(
        'startup-import'
        + '|surface=' + SurfaceNames[Index]
        + '|station-call=' + Ini.Call
        + '|pitch-hz=' + IntToStr(PitchHz));
  finally
    FreeAndNil(MainForm);
    if FileExists(IniPath) and not DeleteFile(IniPath) then
      raise Exception.Create('could not remove the startup-import INI path');
  end;

  if Values.Count <> 2 then
    raise Exception.Create(
      'production legacy import capture emitted an invalid row count');
end;

procedure ObserveLegacySerialRangeErrors(
  const Values: TStrings;
  const Input: TJSONObject);
var
  IniFile: TIniFile;
  IniPath: string;
  Seed: Integer;
begin
  RequireExactObjectFields(Input, ['scenario', 'seed']);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  if Seed <> 12345 then
    raise Exception.Create(
      'serial range error input does not match its pinned contract');

  IniPath := ChangeFileExt(ParamStr(0), '.ini');
  if FileExists(IniPath) and not DeleteFile(IniPath) then
    raise Exception.Create('could not clear the serial range INI path');
  IniFile := TIniFile.Create(IniPath);
  try
    IniFile.WriteString(SEC_STN, 'SerialNrMidContest', 'bad');
    IniFile.WriteString(SEC_STN, 'SerialNrEndContest', '10000-10001');
    IniFile.WriteString(SEC_STN, 'SerialNrCustomRange', '99-1');
  finally
    IniFile.Free;
  end;

  CreateIniLoadForm;
  try
    CapturedIniErrors := Values;
    try
      FromIni(CaptureIniError);
    finally
      CapturedIniErrors := nil;
    end;
    if Values.Count <> 3 then
      raise Exception.Create('CE did not report all serial range errors');
    Values.Add(
      'retained-ranges'
      + '|mid=' + Ini.SerialNRSettings[snMidContest].RangeStr
      + '|end=' + Ini.SerialNRSettings[snEndContest].RangeStr
      + '|custom=' + Ini.SerialNRSettings[snCustomRange].RangeStr);
    Values.Add(
      'custom-caption=' + MainForm.SerialNRCustomRange.Caption);
  finally
    CapturedIniErrors := nil;
    FreeAndNil(MainForm);
    if FileExists(IniPath) and not DeleteFile(IniPath) then
      raise Exception.Create('could not remove the serial range INI path');
  end;

  if Values.Count <> 5 then
    raise Exception.Create(
      'serial range error capture emitted an invalid row count');
end;

var
  AdapterId: string;
  BuildRecipe: string;
  BuildRecipeSha256: string;
  CaseDefinitionSha256: string;
  ExitStatus: Integer;
  Input: TJSONObject;
  InputPath: string;
  InputSha256: string;
  LegacyRoot: string;
  Scenario: string;
  Source: string;
  SourceSha256: string;
  Values: TStringList;
  VersionId: string;

begin
  DefaultFormatSettings.DecimalSeparator := '.';
  ExitStatus := 0;
  Input := nil;
  Values := nil;
  CallCatalog := nil;
  try
    ValidateSha256Implementation;
    ValidateSingleStorage;
    if ParamCount <> 11 then
      raise Exception.Create(
        'usage: LegacyOracle <legacy-root-with-separator> <scenario> '
        + '<adapter-id> <version-id> <source> <source-sha256> '
        + '<build-recipe> <build-recipe-sha256> '
        + '<case-definition-sha256> <input-sha256> <input-json-path>');
    LegacyRoot := ParamStr(1);
    if (LegacyRoot = '')
      or not DirectoryExists(LegacyRoot)
      or not CharInSet(
        LegacyRoot[Length(LegacyRoot)],
        ['\', '/']) then
      raise Exception.Create(
        'legacy root must exist and end with a directory separator');

    Scenario := ParamStr(2);
    AdapterId := ParamStr(3);
    VersionId := ParamStr(4);
    Source := ParamStr(5);
    SourceSha256 := ParamStr(6);
    BuildRecipe := ParamStr(7);
    BuildRecipeSha256 := ParamStr(8);
    CaseDefinitionSha256 := ParamStr(9);
    InputSha256 := ParamStr(10);
    InputPath := ParamStr(11);

    if Scenario <> ExpectedScenario then
      raise Exception.Create('legacy oracle scenario mismatch');
    if AdapterId <> ExpectedAdapterId then
      raise Exception.Create('legacy oracle adapter ID mismatch');
    if VersionId <> ExpectedVersionId then
      raise Exception.Create('legacy oracle version ID mismatch');
    if Source <> ExpectedSource then
      raise Exception.Create('legacy oracle source identity mismatch');
    if BuildRecipe <> ExpectedBuildRecipe then
      raise Exception.Create(
        'legacy oracle build recipe identity mismatch');
    if not IsSha256(SourceSha256)
      or not IsSha256(BuildRecipeSha256)
      or not IsSha256(CaseDefinitionSha256)
      or not IsSha256(InputSha256) then
      raise Exception.Create('legacy oracle binding hash is invalid');

    Input := LoadScenarioInput(InputPath, Scenario, InputSha256);
    Values := TStringList.Create;
    LoadAndValidateCallCatalog(LegacyRoot);
    CreateWidgetset(TOracleNoGuiWidgetSet);
    RegisterNoguiHandlelessClasses;
    Application.Initialize;

    ObserveLegacySerialRangeErrors(Values, Input);
    Emit(
      Scenario,
      AdapterId,
      VersionId,
      Source,
      SourceSha256,
      BuildRecipe,
      BuildRecipeSha256,
      CaseDefinitionSha256,
      InputSha256,
      Values);
  except
    on E: Exception do
    begin
      WriteLn(StdErr, E.ClassName, ': ', E.Message);
      ExitStatus := 4;
    end;
  end;

  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
  begin
    try
      DestroyRuntime;
    except
      on E: Exception do
      begin
        WriteLn(StdErr, E.ClassName, ': cleanup: ', E.Message);
        ExitStatus := 4;
      end;
    end;
  end;
  CallCatalog.Free;
  Values.Free;
  Input.Free;
  FreeWidgetSet;
  if ExitStatus <> 0 then
    Halt(ExitStatus);
end.
