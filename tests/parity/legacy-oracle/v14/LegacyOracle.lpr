program LegacyOracle;

{$mode Delphi}{$H+}
{$apptype console}

uses
  InterfaceBase,
  NoGUIInt,
  NoGUIWSFactory,
  Forms,
  LCLType,
  Types,
  Controls,
  StdCtrls,
  ExtCtrls,
  fpjson,
  jsonparser,
  WSLCLClasses,
  WSControls,
  WSForms,
  WSExtCtrls,
  WSStdCtrls,
  SysUtils,
  Classes,
  Math,
  Ini,
  DXCC,
  Main,
  Contest,
  Station,
  MyStn,
  DxStn,
  MorseKey,
  SndTypes,
  WavFile;

const
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v14';
  ExpectedSource =
    'tests/parity/legacy-oracle/v14/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v14/build-recipe.json';
  ExpectedScenario =
    'audio.qrn-background-sparse-impulses-seed-12345';
  ExpectedSampleRate = 11025;
  ExpectedBlockSize = 512;
  ExpectedSeed = 12345;
  ExpectedBandwidthHz = 500;
  ExpectedPitchHz = 600;
  ExpectedStartupRequestCount = 5;
  ExpectedComparedBlockCount = 1;
  ExpectedValueCount = 7;
  ExpectedProbeCount = 13;
  ExpectedReplacementCount = 6;
  ExpectedBurstTriggerOrdinal = 1542;
  ExpectedCleanTerminalRandomOrdinal = 1024;
  ExpectedQrnTerminalRandomOrdinal = 1543;
  ExpectedFirstDivergence = 310;

  ExpectedProbeSampleIndexes:
    array[0..ExpectedProbeCount - 1] of Integer = (
      0,
      91,
      92,
      93,
      248,
      309,
      310,
      311,
      323,
      482,
      488,
      507,
      511
    );
  ExpectedReplacementSampleIndexes:
    array[0..ExpectedReplacementCount - 1] of Integer = (
      92,
      248,
      323,
      482,
      488,
      507
    );
  ExpectedTriggerRandomOrdinals:
    array[0..ExpectedReplacementCount - 1] of Integer = (
      1116,
      1273,
      1349,
      1509,
      1516,
      1536
    );
  ExpectedReplacementRandomOrdinals:
    array[0..ExpectedReplacementCount - 1] of Integer = (
      1117,
      1274,
      1350,
      1510,
      1517,
      1537
    );

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;
  TIntegerArray = array of Integer;
  TExtendedArray = array of Extended;
  TSingleBlockArray = array of TSingleArray;

  TNoiseRunObservation = record
    Blocks: TSingleBlockArray;
    StationCount: Integer;
    TerminalRandom: Single;
  end;

  TQrnDecisionObservation = record
    ReplacementSampleIndexes: TIntegerArray;
    TriggerRandomOrdinals: TIntegerArray;
    TriggerValues: TExtendedArray;
    ReplacementRandomOrdinals: TIntegerArray;
    ReplacementRandomValues: TExtendedArray;
    ReplacementSamples: TSingleArray;
    BurstTriggerOrdinal: Integer;
    BurstTriggerValue: Extended;
    BurstCreated: Boolean;
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

  TFailClosedContest = class(TContest)
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

var
  AbstractStubCalls: Integer;

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

procedure StubCalled(const Name: string);
begin
  Inc(AbstractStubCalls);
  raise Exception.Create('fail-if-called contest stub: ' + Name);
end;

function TFailClosedContest.LoadCallHistory(
  const AUserCallsign: string): Boolean;
begin
  StubCalled('LoadCallHistory');
  Result := False;
end;

function TFailClosedContest.PickStation: Integer;
begin
  StubCalled('PickStation');
  Result := 0;
end;

procedure TFailClosedContest.DropStation(Id: Integer);
begin
  StubCalled('DropStation');
end;

function TFailClosedContest.GetCall(Id: Integer): string;
begin
  StubCalled('GetCall');
  Result := '';
end;

procedure TFailClosedContest.GetExchange(
  Id: Integer;
  out AStation: TDxStation);
begin
  StubCalled('GetExchange');
  AStation := nil;
end;

procedure ConfigureIni(const QrnEnabled: Boolean);
begin
  Ini.Call := 'W7SST';
  Ini.Wpm := 30;
  Ini.Bandwidth := ExpectedBandwidthHz;
  Ini.Pitch := ExpectedPitchHz;
  Ini.Qsk := False;
  Ini.Rit := 0;
  Ini.BufSize := ExpectedBlockSize;
  Ini.Activity := 1;
  Ini.Qrn := QrnEnabled;
  Ini.Qrm := False;
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
  const QrnEnabled: Boolean);
var
  BandwidthIndex: Integer;
begin
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime globals were not initially clear');

  AbstractStubCalls := 0;
  ConfigureIni(QrnEnabled);
  RandSeed := Seed;
  gDXCCList := TDXCC.Create;
  MakeKeyer(ExpectedSampleRate, ExpectedBlockSize);
  Tst := TFailClosedContest.Create;
  MainForm := TMainForm.CreateNew(nil);
  MainForm.Panel2 := TPanel.Create(MainForm);
  MainForm.Panel4 := TPanel.Create(MainForm);
  MainForm.Panel7 := TPanel.Create(MainForm);
  MainForm.Panel8 := TPanel.Create(MainForm);
  MainForm.Panel8.Width := 100;
  MainForm.Shape2 := TShape.Create(MainForm);
  MainForm.ComboBox2 := TComboBox.Create(MainForm);
  for BandwidthIndex := 0 to 10 do
    MainForm.ComboBox2.Items.Add(
      IntToStr(100 + BandwidthIndex * 50) + ' Hz');
  MainForm.AlWavFile1 := TAlWavFile.Create(MainForm);
  MainForm.SetBw((ExpectedBandwidthHz - 100) div 50);

  if (Tst.ClassType <> TFailClosedContest)
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
    or (Ini.Qrn <> QrnEnabled)
    or Ini.Qrm
    or Ini.Qsb
    or Ini.Flutter
    or Ini.Lids
    or Ini.Qsk
    or (MainForm.ComboBox2.ItemIndex <> 8)
    or (Tst.Filt.Points <> 15)
    or (Tst.Filt2.Points <> Tst.Filt.Points)
    or (AbstractStubCalls <> 0) then
    raise Exception.Create('CE pure receiver runtime is not active');
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

function SingleArraysEqual(
  const First: TSingleArray;
  const Second: TSingleArray): Boolean;
var
  Index: Integer;
begin
  Result := Length(First) = Length(Second);
  if not Result then
    Exit;
  for Index := 0 to High(First) do
    if SingleBits(First[Index]) <> SingleBits(Second[Index]) then
      Exit(False);
end;

function FirstSingleArrayDifference(
  const First: TSingleArray;
  const Second: TSingleArray): Integer;
var
  Index: Integer;
begin
  for Index := 0 to Min(High(First), High(Second)) do
    if SingleBits(First[Index]) <> SingleBits(Second[Index]) then
      Exit(Index);
  if Length(First) <> Length(Second) then
    Exit(Min(Length(First), Length(Second)));
  Result := -1;
end;

function JoinIntegerArray(const Values: TIntegerArray): string;
var
  Index: Integer;
begin
  Result := '';
  for Index := 0 to High(Values) do
  begin
    if Index > 0 then
      Result := Result + ',';
    Result := Result + IntToStr(Values[Index]);
  end;
end;

function JoinExtendedValues(const Values: TExtendedArray): string;
var
  Index: Integer;
begin
  Result := '';
  for Index := 0 to High(Values) do
  begin
    if Index > 0 then
      Result := Result + ',';
    Result := Result + FloatValue(Values[Index]);
  end;
end;

function RoundToSingle(const Value: Extended): Single;
begin
  Result := Value;
end;

function JoinExtendedSingleBits(
  const Values: TExtendedArray): string;
var
  Index: Integer;
begin
  Result := '';
  for Index := 0 to High(Values) do
  begin
    if Index > 0 then
      Result := Result + ',';
    Result := Result + SingleBits(RoundToSingle(Values[Index]));
  end;
end;

function JoinSingleBits(const Values: TSingleArray): string;
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

function ReplayRandomSingleAtOrdinal(
  const Seed: Integer;
  const Ordinal: Integer): Single;
var
  Index: Integer;
  Value: Extended;
begin
  RandSeed := Seed;
  Value := 0;
  for Index := 0 to Ordinal do
    Value := Random;
  Result := Value;
end;

function ReplayQrnDecisions(
  const Seed: Integer;
  const BlockSize: Integer): TQrnDecisionObservation;
const
  NoiseAmplitude = 6000;
var
  Discarded: Extended;
  DecisionIndex: Integer;
  Ordinal: Integer;
  ReplacementRandom: Extended;
  SampleIndex: Integer;
  TriggerRandom: Extended;
begin
  SetLength(Result.ReplacementSampleIndexes, 0);
  SetLength(Result.TriggerRandomOrdinals, 0);
  SetLength(Result.TriggerValues, 0);
  SetLength(Result.ReplacementRandomOrdinals, 0);
  SetLength(Result.ReplacementRandomValues, 0);
  SetLength(Result.ReplacementSamples, 0);
  Result.BurstTriggerOrdinal := -1;
  Result.BurstTriggerValue := 0;
  Result.BurstCreated := False;
  Result.TerminalRandom := 0;

  RandSeed := Seed;
  Ordinal := 0;
  for SampleIndex := 0 to BlockSize - 1 do
  begin
    Discarded := Random;
    Inc(Ordinal);
    Discarded := Random;
    Inc(Ordinal);
  end;
  if (Ordinal <> BlockSize * 2) or (Discarded < 0) then
    raise Exception.Create('CE hiss decision replay framing changed');

  for SampleIndex := 0 to BlockSize - 1 do
  begin
    TriggerRandom := Random;
    if TriggerRandom < 0.01 then
    begin
      DecisionIndex := Length(Result.ReplacementSampleIndexes);
      SetLength(
        Result.ReplacementSampleIndexes,
        DecisionIndex + 1);
      SetLength(Result.TriggerRandomOrdinals, DecisionIndex + 1);
      SetLength(Result.TriggerValues, DecisionIndex + 1);
      SetLength(
        Result.ReplacementRandomOrdinals,
        DecisionIndex + 1);
      SetLength(
        Result.ReplacementRandomValues,
        DecisionIndex + 1);
      SetLength(Result.ReplacementSamples, DecisionIndex + 1);

      Result.ReplacementSampleIndexes[DecisionIndex] :=
        SampleIndex;
      Result.TriggerRandomOrdinals[DecisionIndex] := Ordinal;
      Result.TriggerValues[DecisionIndex] := TriggerRandom;
      Inc(Ordinal);

      ReplacementRandom := Random;
      Result.ReplacementRandomOrdinals[DecisionIndex] := Ordinal;
      Result.ReplacementRandomValues[DecisionIndex] :=
        ReplacementRandom;
      Result.ReplacementSamples[DecisionIndex] :=
        60 * NoiseAmplitude * (ReplacementRandom - 0.5);
      Inc(Ordinal);
    end
    else
      Inc(Ordinal);
  end;

  Result.BurstTriggerOrdinal := Ordinal;
  Result.BurstTriggerValue := Random;
  Result.BurstCreated := Result.BurstTriggerValue < 0.01;
  Inc(Ordinal);
  Result.TerminalRandom := Random;

  if (Length(Result.ReplacementSampleIndexes)
      <> ExpectedReplacementCount)
    or (Result.BurstTriggerOrdinal
        <> ExpectedBurstTriggerOrdinal)
    or Result.BurstCreated
    or (Ordinal <> ExpectedQrnTerminalRandomOrdinal) then
    raise Exception.Create(
      'CE QRN decision replay no longer matches the pinned vector');
end;

function ObserveNoiseRun(
  const Seed: Integer;
  const BlockSize: Integer;
  const ComparedBlockCount: Integer;
  const QrnEnabled: Boolean): TNoiseRunObservation;
var
  BlockData: TSingleArray;
  BlockIndex: Integer;
  SampleIndex: Integer;
  StartupIndex: Integer;
begin
  CreateRuntime(Seed, QrnEnabled);
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
        or (Ini.Qrn <> QrnEnabled)
        or Ini.Qrm
        or Ini.Qsb
        or Ini.Flutter
        or Ini.Lids
        or Ini.Qsk
        or (AbstractStubCalls <> 0) then
        raise Exception.Create(
          'CE startup request framing changed');
    end;

    SetLength(Result.Blocks, ComparedBlockCount);
    for BlockIndex := 0 to ComparedBlockCount - 1 do
    begin
      BlockData := Tst.GetAudio;
      if (Length(BlockData) <> BlockSize)
        or (Tst.BlockNumber
            <> ExpectedStartupRequestCount + BlockIndex + 1)
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
        or (Ini.Qrn <> QrnEnabled)
        or Ini.Qrm
        or Ini.Qsb
        or Ini.Flutter
        or Ini.Lids
        or Ini.Qsk
        or (MainForm.ComboBox2.ItemIndex <> 8)
        or (Tst.Filt.Points <> 15)
        or (Tst.Filt2.Points <> Tst.Filt.Points)
        or (AbstractStubCalls <> 0) then
        raise Exception.Create(
          'CE QRN background capture left the pure receiver path');

      SetLength(Result.Blocks[BlockIndex], BlockSize);
      for SampleIndex := 0 to BlockSize - 1 do
        Result.Blocks[BlockIndex][SampleIndex] :=
          NormalizeSample(BlockData[SampleIndex]);
    end;

    Result.StationCount := Tst.Stations.Count;
    if Result.StationCount <> 0 then
      raise Exception.Create(
        'CE QRN background capture created a burst station');
    Result.TerminalRandom := Random;
  finally
    DestroyRuntime;
  end;
end;

procedure ObserveQrnBackgroundSparseImpulses(
  const Values: TStrings;
  const Input: TJSONObject);
var
  BandwidthHz: Integer;
  BlockIndex: Integer;
  BlockSize: Integer;
  BurstTriggerRandomOrdinal: Integer;
  Clean: TNoiseRunObservation;
  CleanTerminalRandomOrdinal: Integer;
  ComparedBlockCount: Integer;
  Decisions: TQrnDecisionObservation;
  ExactEqual: Boolean;
  FirstDivergence: Integer;
  PitchHz: Integer;
  ProbeIndex: Integer;
  ProbeSampleIndexes: TIntegerArray;
  ProbeText: string;
  QrnRun: TNoiseRunObservation;
  QrnTerminalRandomOrdinal: Integer;
  ReplacementRandomOrdinals: TIntegerArray;
  ReplacementSampleIndexes: TIntegerArray;
  SampleRate: Integer;
  Seed: Integer;
  StartupRequestCount: Integer;
  TriggerRandomOrdinals: TIntegerArray;
begin
  RequireExactObjectFields(
    Input,
    [
      'bandwidthHz',
      'blockSize',
      'burstTriggerRandomOrdinal',
      'cleanTerminalRandomOrdinal',
      'comparedBlockCount',
      'pitchHz',
      'probeSampleIndexes',
      'qrnTerminalRandomOrdinal',
      'replacementRandomOrdinals',
      'replacementSampleIndexes',
      'sampleRate',
      'scenario',
      'seed',
      'startupRequestCount',
      'triggerRandomOrdinals'
    ]);
  SampleRate := RequireInteger(Input, 'sampleRate', 1, MaxInt);
  BlockSize := RequireInteger(Input, 'blockSize', 1, MaxInt);
  Seed := RequireInteger(Input, 'seed', Low(Integer), MaxInt);
  BandwidthHz := RequireInteger(
    Input,
    'bandwidthHz',
    1,
    MaxInt);
  PitchHz := RequireInteger(Input, 'pitchHz', 1, MaxInt);
  StartupRequestCount := RequireInteger(
    Input,
    'startupRequestCount',
    1,
    MaxInt);
  ComparedBlockCount := RequireInteger(
    Input,
    'comparedBlockCount',
    1,
    MaxInt);
  BurstTriggerRandomOrdinal := RequireInteger(
    Input,
    'burstTriggerRandomOrdinal',
    0,
    MaxInt);
  CleanTerminalRandomOrdinal := RequireInteger(
    Input,
    'cleanTerminalRandomOrdinal',
    0,
    MaxInt);
  QrnTerminalRandomOrdinal := RequireInteger(
    Input,
    'qrnTerminalRandomOrdinal',
    0,
    MaxInt);
  ProbeSampleIndexes := RequireIntegerArray(
    Input,
    'probeSampleIndexes',
    0,
    BlockSize - 1);
  ReplacementSampleIndexes := RequireIntegerArray(
    Input,
    'replacementSampleIndexes',
    0,
    BlockSize - 1);
  TriggerRandomOrdinals := RequireIntegerArray(
    Input,
    'triggerRandomOrdinals',
    0,
    MaxInt);
  ReplacementRandomOrdinals := RequireIntegerArray(
    Input,
    'replacementRandomOrdinals',
    0,
    MaxInt);

  if (SampleRate <> ExpectedSampleRate)
    or (BlockSize <> ExpectedBlockSize)
    or (Seed <> ExpectedSeed)
    or (BandwidthHz <> ExpectedBandwidthHz)
    or (PitchHz <> ExpectedPitchHz)
    or (StartupRequestCount <> ExpectedStartupRequestCount)
    or (ComparedBlockCount <> ExpectedComparedBlockCount)
    or (BurstTriggerRandomOrdinal
        <> ExpectedBurstTriggerOrdinal)
    or (CleanTerminalRandomOrdinal
        <> ExpectedCleanTerminalRandomOrdinal)
    or (QrnTerminalRandomOrdinal
        <> ExpectedQrnTerminalRandomOrdinal)
    or (Length(ProbeSampleIndexes) <> ExpectedProbeCount)
    or (Length(ReplacementSampleIndexes)
        <> ExpectedReplacementCount)
    or (Length(TriggerRandomOrdinals)
        <> ExpectedReplacementCount)
    or (Length(ReplacementRandomOrdinals)
        <> ExpectedReplacementCount) then
    raise Exception.Create(
      'QRN background input does not match its contract');
  for ProbeIndex := 0 to ExpectedProbeCount - 1 do
    if ProbeSampleIndexes[ProbeIndex]
        <> ExpectedProbeSampleIndexes[ProbeIndex] then
      raise Exception.Create(
        'QRN background probes do not match their contract');
  for ProbeIndex := 0 to ExpectedReplacementCount - 1 do
    if (ReplacementSampleIndexes[ProbeIndex]
        <> ExpectedReplacementSampleIndexes[ProbeIndex])
      or (TriggerRandomOrdinals[ProbeIndex]
          <> ExpectedTriggerRandomOrdinals[ProbeIndex])
      or (ReplacementRandomOrdinals[ProbeIndex]
          <> ExpectedReplacementRandomOrdinals[ProbeIndex]) then
      raise Exception.Create(
        'QRN background decision ordinals do not match their contract');

  ProbeText := '';
  for ProbeIndex := 0 to High(ProbeSampleIndexes) do
  begin
    if ProbeIndex > 0 then
      ProbeText := ProbeText + ',';
    ProbeText :=
      ProbeText + IntToStr(ProbeSampleIndexes[ProbeIndex]);
  end;
  Values.Add(
    'configuration'
    + '|sample-rate=' + IntToStr(SampleRate)
    + '|block-size=' + IntToStr(BlockSize)
    + '|seed=' + IntToStr(Seed)
    + '|bandwidth-hz=' + IntToStr(BandwidthHz)
    + '|pitch-hz=' + IntToStr(PitchHz)
    + '|startup-request-count='
    + IntToStr(StartupRequestCount)
    + '|compared-block-count='
    + IntToStr(ComparedBlockCount)
    + '|probe-sample-indexes=' + ProbeText
    + '|fresh-runs=clean,qrn'
    + '|run-mode=rmStop'
    + '|qsb=false'
    + '|flutter=false'
    + '|qrm=false'
    + '|qsk=false'
    + '|lids=false'
    + '|operator-transmission=false'
    + '|normal-dx-stations=false'
    + '|normalization=ce-single-div-32768-clamp-unit');

  Decisions := ReplayQrnDecisions(Seed, BlockSize);
  for ProbeIndex := 0 to ExpectedReplacementCount - 1 do
    if (Decisions.ReplacementSampleIndexes[ProbeIndex]
        <> ReplacementSampleIndexes[ProbeIndex])
      or (Decisions.TriggerRandomOrdinals[ProbeIndex]
          <> TriggerRandomOrdinals[ProbeIndex])
      or (Decisions.ReplacementRandomOrdinals[ProbeIndex]
          <> ReplacementRandomOrdinals[ProbeIndex]) then
      raise Exception.Create(
        'CE QRN decision replay diverged from its input contract');
  Values.Add(
    'qrn-background-decisions'
    + '|replacement-indexes='
    + JoinIntegerArray(Decisions.ReplacementSampleIndexes)
    + '|trigger-ordinals='
    + JoinIntegerArray(Decisions.TriggerRandomOrdinals)
    + '|trigger-values='
    + JoinExtendedValues(Decisions.TriggerValues)
    + '|trigger-single-bits='
    + JoinExtendedSingleBits(Decisions.TriggerValues)
    + '|replacement-value-ordinals='
    + JoinIntegerArray(Decisions.ReplacementRandomOrdinals)
    + '|replacement-random-values='
    + JoinExtendedValues(Decisions.ReplacementRandomValues)
    + '|replacement-random-single-bits='
    + JoinExtendedSingleBits(Decisions.ReplacementRandomValues)
    + '|replacement-sample-bits='
    + JoinSingleBits(Decisions.ReplacementSamples)
    + '|burst-trigger-ordinal='
    + IntToStr(Decisions.BurstTriggerOrdinal)
    + '|burst-trigger-value='
    + FloatValue(Decisions.BurstTriggerValue)
    + '|burst-trigger-single-bits='
    + SingleBits(RoundToSingle(Decisions.BurstTriggerValue))
    + '|burst-created='
    + LowerCase(BoolToStr(Decisions.BurstCreated, True)));

  Clean := ObserveNoiseRun(
    Seed,
    BlockSize,
    ComparedBlockCount,
    False);
  QrnRun := ObserveNoiseRun(
    Seed,
    BlockSize,
    ComparedBlockCount,
    True);

  if (SingleBits(Clean.TerminalRandom)
      <> SingleBits(
           ReplayRandomSingleAtOrdinal(
             Seed,
             CleanTerminalRandomOrdinal)))
    or (SingleBits(QrnRun.TerminalRandom)
        <> SingleBits(Decisions.TerminalRandom)) then
    raise Exception.Create(
      'CE terminal random sentinel did not match its replayed ordinal');

  for BlockIndex := 0 to ComparedBlockCount - 1 do
  begin
    ProbeText := '';
    for ProbeIndex := 0 to High(ProbeSampleIndexes) do
    begin
      if ProbeIndex > 0 then
        ProbeText := ProbeText + ',';
      ProbeText := ProbeText
        + SingleBits(
            Clean.Blocks[BlockIndex][
              ProbeSampleIndexes[ProbeIndex]]);
    end;
    Values.Add(
      'clean-block[' + IntToStr(BlockIndex) + ']'
      + '|sample-count=' + IntToStr(BlockSize)
      + '|probe-bits=' + ProbeText
      + '|float-sha256='
      + FloatBlockSha256(Clean.Blocks[BlockIndex]));
  end;

  for BlockIndex := 0 to ComparedBlockCount - 1 do
  begin
    ProbeText := '';
    for ProbeIndex := 0 to High(ProbeSampleIndexes) do
    begin
      if ProbeIndex > 0 then
        ProbeText := ProbeText + ',';
      ProbeText := ProbeText
        + SingleBits(
            QrnRun.Blocks[BlockIndex][
              ProbeSampleIndexes[ProbeIndex]]);
    end;
    Values.Add(
      'qrn-block[' + IntToStr(BlockIndex) + ']'
      + '|sample-count=' + IntToStr(BlockSize)
      + '|probe-bits=' + ProbeText
      + '|float-sha256='
      + FloatBlockSha256(QrnRun.Blocks[BlockIndex]));
  end;

  Values.Add(
    'station-counts'
    + '|clean=' + IntToStr(Clean.StationCount)
    + '|qrn=' + IntToStr(QrnRun.StationCount)
    + '|burst-created='
    + LowerCase(BoolToStr(Decisions.BurstCreated, True)));

  Values.Add(
    'terminal-random-sentinels'
    + '|clean-next-ordinal='
    + IntToStr(CleanTerminalRandomOrdinal)
    + '|clean-value=' + FloatValue(Clean.TerminalRandom)
    + '|clean-single-bits=' + SingleBits(Clean.TerminalRandom)
    + '|qrn-next-ordinal='
    + IntToStr(QrnTerminalRandomOrdinal)
    + '|qrn-value=' + FloatValue(QrnRun.TerminalRandom)
    + '|qrn-single-bits=' + SingleBits(QrnRun.TerminalRandom));

  for BlockIndex := 0 to ComparedBlockCount - 1 do
  begin
    ExactEqual := SingleArraysEqual(
      Clean.Blocks[BlockIndex],
      QrnRun.Blocks[BlockIndex]);
    FirstDivergence := FirstSingleArrayDifference(
      Clean.Blocks[BlockIndex],
      QrnRun.Blocks[BlockIndex]);
    Values.Add(
      'output-difference[' + IntToStr(BlockIndex) + ']'
      + '|clean-float-sha256='
      + FloatBlockSha256(Clean.Blocks[BlockIndex])
      + '|qrn-float-sha256='
      + FloatBlockSha256(QrnRun.Blocks[BlockIndex])
      + '|exact-equal='
      + LowerCase(BoolToStr(ExactEqual, True))
      + '|first-divergence=' + IntToStr(FirstDivergence));
    if ExactEqual
      or (FirstDivergence <> ExpectedFirstDivergence) then
      raise Exception.Create(
        'CE QRN output divergence changed from the pinned vector: '
        + IntToStr(FirstDivergence));
  end;

  if Values.Count <> ExpectedValueCount then
    raise Exception.Create(
      'QRN background capture emitted an invalid row count');
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
  try
    ValidateSha256Implementation;
    ValidateSingleStorage;
    if ParamCount <> 11 then
      raise Exception.Create(
        'usage: LegacyOracle <legacy-root-with-separator> <scenario> '
        + '<adapter-id> <version-id> <source> <source-sha256> '
        + '<build-recipe> <build-recipe-sha256> '
        + '<case-definition-sha256> <input-sha256> <input-json-path>');
    if (ParamStr(1) = '')
      or not DirectoryExists(ParamStr(1))
      or not CharInSet(
        ParamStr(1)[Length(ParamStr(1))],
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
    CreateWidgetset(TOracleNoGuiWidgetSet);
    RegisterNoguiHandlelessClasses;
    Application.Initialize;

    ObserveQrnBackgroundSparseImpulses(Values, Input);
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
  Values.Free;
  Input.Free;
  FreeWidgetSet;
  if ExitStatus <> 0 then
    Halt(ExitStatus);
end.
