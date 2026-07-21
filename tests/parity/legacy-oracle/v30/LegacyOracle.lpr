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
  ExtCtrls,
  StdCtrls,
  Spin,
  Graphics,
  fpjson,
  jsonparser,
  WSLCLClasses,
  WSControls,
  WSForms,
  WSExtCtrls,
  WSStdCtrls,
  WSSpin,
  SysUtils,
  Classes,
  Ini,
  DXCC,
  Main,
  Contest,
  CqWpx,
  CWOPS,
  ArrlFd,
  NaQp,
  CqWW,
  ArrlDx,
  CWSST,
  ALLJA,
  ACAG,
  IaruHf,
  ArrlSS,
  Station,
  MyStn,
  MorseKey,
  SndTypes,
  ExchFields,
  Log;

const
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v30';
  ExpectedSource =
    'tests/parity/legacy-oracle/v30/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v30/build-recipe.json';
  ExpectedScenario =
    'contest.default-two-field-remote-exchange-format-seed-12345';
  ExpectedStationCall = 'W7SST';
  ExpectedRemoteCall = 'K1ABC';
  ExpectedRst = 599;
  ExpectedArrlDxExchange2 = 'MA';
  ExpectedAllJaExchange2 = '12H';
  ExpectedAcagExchange2 = '1234H';
  ExpectedIaruHfExchange2 = 'ARRL';
  ExpectedSeed = 12345;
  ExpectedStationIdRate = 3;
  ExpectedContestCount = 12;

  ExpectedContestIds: array[0..ExpectedContestCount - 1] of string = (
    'scWpx',
    'scCwt',
    'scFieldDay',
    'scNaQp',
    'scHst',
    'scCQWW',
    'scArrlDx',
    'scSst',
    'scAllJa',
    'scAcag',
    'scIaruHf',
    'scArrlSS'
  );
  ExpectedRunModeIds: array[0..ExpectedContestCount - 1] of string = (
    'rmPileup',
    'rmPileup',
    'rmPileup',
    'rmPileup',
    'rmHst',
    'rmPileup',
    'rmPileup',
    'rmPileup',
    'rmPileup',
    'rmPileup',
    'rmPileup',
    'rmPileup'
  );
  ExpectedQsoCalls: array[0..ExpectedContestCount - 1] of string = (
    'K1ABC', 'K1ABC', 'K1ABC', 'K1ABC', 'K1ABC', 'DL1ABC',
    'DL1ABC', 'K1ABC', 'JA1ABC', 'JA1ABC', 'DL1ABC', 'K1ABC'
  );
  ExpectedQsoRsts: array[0..ExpectedContestCount - 1] of string = (
    '5NN', '5NN', '5NN', '5NN', '5NN', '5NN',
    '5NN', '5NN', '5NN', '5NN', '5NN', '5NN'
  );
  ExpectedQsoExchange1: array[0..ExpectedContestCount - 1] of string = (
    '123', 'DAVID', '3A', 'ALEX', '123', '',
    '', 'BRUCE', '', '', '', '123 A'
  );
  ExpectedQsoExchange2: array[0..ExpectedContestCount - 1] of string = (
    '', '123', 'OR', 'ON', '', '14',
    'KW', 'MA', '10H', '1002H', '28', '72 OR'
  );
  ExpectedActionCount = 6;

  ExpectedActionIds: array[0..ExpectedActionCount - 1] of string = (
    'empty',
    'short-partial',
    'uncertain',
    'corrected',
    'same-call-repeat',
    'complete'
  );
  ExpectedActionCalls: array[0..ExpectedActionCount - 1] of string = (
    '',
    'K1',
    'K1A?',
    'K1ABC',
    'K1ABC',
    'K2XYZ'
  );
  ExpectedActionResets: array[0..ExpectedActionCount - 1] of Boolean = (
    True,
    True,
    True,
    False,
    False,
    True
  );

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;

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

  TOracleStation = class(TStation)
  public
    procedure ProcessEvent(AEvent: TStationEvent); override;
    function ObserveNrAsText: string;
  end;

procedure TOracleStation.ProcessEvent(AEvent: TStationEvent);
begin
end;

function TOracleStation.ObserveNrAsText: string;
begin
  Result := NrAsText;
end;

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
  if FindWSRegistered(TCustomEdit) = nil then
    RegisterWSComponent(TCustomEdit, TWSCustomEdit);
  if FindWSRegistered(TCustomFloatSpinEdit) = nil then
    RegisterWSComponent(
      TCustomFloatSpinEdit,
      TWSCustomFloatSpinEdit);
end;

function JsonEscape(const Value: string): string;
begin
  Result := StringReplace(Value, '\', '\\', [rfReplaceAll]);
  Result := StringReplace(Result, '"', '\"', [rfReplaceAll]);
  Result := StringReplace(Result, #13, '\r', [rfReplaceAll]);
  Result := StringReplace(Result, #10, '\n', [rfReplaceAll]);
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
end;

function RequireBoolean(
  const Input: TJSONObject;
  const Name: string): Boolean;
var
  Data: TJSONData;
begin
  Data := Input.Find(Name);
  if (Data = nil) or (Data.JSONType <> jtBoolean) then
    raise Exception.Create(Name + ' is not a boolean');
  Result := Data.AsBoolean;
end;

function RequireActions(
  const Input: TJSONObject): TJSONArray;
var
  Action: TJSONObject;
  Data: TJSONData;
  Index: Integer;
begin
  Data := Input.Find('actions');
  if (Data = nil) or (Data.JSONType <> jtArray) then
    raise Exception.Create('actions is not an array');
  Result := TJSONArray(Data);
  if Result.Count <> ExpectedActionCount then
    raise Exception.Create('actions has an unsupported count');

  for Index := 0 to Result.Count - 1 do
  begin
    Data := Result.Items[Index];
    if not (Data is TJSONObject) then
      raise Exception.Create('an action is not an object');
    Action := TJSONObject(Data);
    RequireExactObjectFields(Action, ['call', 'id', 'reset']);
    if RequireString(Action, 'id') <> ExpectedActionIds[Index] then
      raise Exception.Create('action id does not match the fixed vector');
    if RequireString(Action, 'call') <> ExpectedActionCalls[Index] then
      raise Exception.Create('action call does not match the fixed vector');
    if RequireBoolean(Action, 'reset')
        <> ExpectedActionResets[Index] then
      raise Exception.Create('action reset does not match the fixed vector');
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

procedure ConfigureIni(const ContestIndex: Integer);
begin
  Ini.Call := ExpectedStationCall;
  Ini.Wpm := 25;
  Ini.Bandwidth := 500;
  Ini.Pitch := 600;
  Ini.Qsk := False;
  Ini.Rit := 0;
  Ini.RitStepIncr := 50;
  Ini.BufSize := 512;
  Ini.Activity := 0;
  Ini.Qrn := False;
  Ini.Qrm := False;
  Ini.Qsb := False;
  Ini.Flutter := False;
  Ini.Lids := False;
  Ini.Duration := 30;
  if ContestIndex = Ord(scHst) then
  begin
    Ini.RunMode := rmHst;
    Ini.DefaultRunMode := rmHst;
  end
  else if ContestIndex = Ord(scWpx) then
  begin
    Ini.RunMode := rmWpx;
    Ini.DefaultRunMode := rmWpx;
  end
  else
  begin
    Ini.RunMode := rmPileup;
    Ini.DefaultRunMode := rmPileup;
  end;

  Ini.SaveWav := False;
  Ini.FarnsworthCharRate := 25;
  Ini.AllStationsWpmS := 0;
  Ini.CallsFromKeyer := False;
  Ini.DebugExchSettings := False;
  Ini.DebugCwDecoder := False;
  Ini.DebugGhosting := False;
  Ini.ShowExchangeSummary := 0;
  Ini.SerialNR := snMidContest;
  Ini.StationIdRate := ExpectedStationIdRate;
  Ini.SimContest := TSimContest(ContestIndex);
  Ini.ActiveContest := @Ini.ContestDefinitions[Ini.SimContest];
end;

function CreateContest(const ContestIndex: Integer): TContest;
begin
  case TSimContest(ContestIndex) of
    scWpx: Result := TCqWpx.Create;
    scCwt: Result := TCWOPS.Create;
    scFieldDay: Result := TArrlFieldDay.Create;
    scNaQp: Result := TNcjNaQp.Create;
    scHst: Result := TCqWpx.Create;
    scCQWW: Result := TCqWw.Create;
    scArrlDx: Result := TArrlDx.Create;
    scSst: Result := TCWSST.Create;
    scAllJa: Result := TALLJA.Create;
    scAcag: Result := TACAG.Create;
    scIaruHf: Result := TIaruHf.Create;
    scArrlSS: Result := TSweepstakes.Create;
  else
    raise Exception.Create('unsupported contest index');
  end;
end;

procedure CreateHandlelessMainForm;
begin
  MainForm := TMainForm.CreateNew(nil);

  MainForm.Edit1 := TEdit.Create(MainForm);
  MainForm.Edit1.Parent := MainForm;
  MainForm.Edit1.OnChange := MainForm.Edit1Change;
  MainForm.Edit1.OnEnter := MainForm.Edit1Enter;

  MainForm.Edit2 := TEdit.Create(MainForm);
  MainForm.Edit2.Parent := MainForm;

  MainForm.Edit3 := TEdit.Create(MainForm);
  MainForm.Edit3.Parent := MainForm;

  MainForm.SpinEdit1 := TSpinEdit.Create(MainForm);
  MainForm.SpinEdit1.Parent := MainForm;
  MainForm.SpinEdit1.MinValue := 10;
  MainForm.SpinEdit1.MaxValue := 120;
  MainForm.SpinEdit1.Value := Ini.Wpm;

  MainForm.sbar := TPanel.Create(MainForm);
  MainForm.sbar.Parent := MainForm;
  MainForm.ActiveControl := MainForm.Edit1;
end;

procedure RequireNoHandles;
begin
  if MainForm.HandleAllocated
    or MainForm.Edit1.HandleAllocated
    or MainForm.Edit2.HandleAllocated
    or MainForm.Edit3.HandleAllocated
    or MainForm.SpinEdit1.HandleAllocated
    or MainForm.sbar.HandleAllocated then
    raise Exception.Create('a control handle was allocated');
end;

procedure CreateRuntime(const ContestIndex: Integer);
var
  ErrorText: string;
begin
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime globals were not initially clear');

  ConfigureIni(ContestIndex);
  RandSeed := ExpectedSeed;
  QsoList := nil;
  CallSent := False;
  NrSent := False;
  SBarDebugMsg := '';
  SBarStationInfo := '';
  SBarSummaryMsg := '';
  SBarErrorMsg := '';
  SBarErrorColor := clDefault;
  BDebugExchSettings := False;
  BDebugCwDecoder := False;
  BDebugGhosting := False;

  gDXCCList := TDXCC.Create;
  MakeKeyer(DEFAULTRATE, Ini.BufSize);
  Tst := CreateContest(ContestIndex);
  CreateHandlelessMainForm;

  ErrorText := '';
  if not Tst.OnSetMyCall(ExpectedStationCall, ErrorText) then
    raise Exception.Create('CE rejected the fixed station call: ' + ErrorText);
  MainForm.RecvExchTypes :=
    Tst.GetRecvExchTypes(
      skMyStation,
      Tst.Me.MyCall,
      '');

  if (Tst.Me.ClassType <> TMyStation)
    or (Tst.Me.MyCall <> ExpectedStationCall)
    or (Tst.Me.State <> stListening)
    or (Tst.Stations.Count <> 0) then
    raise Exception.Create('CE contest-message runtime is not configured');
  RequireNoHandles;
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
  QsoList := nil;
  CallSent := False;
  NrSent := False;

  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime teardown was incomplete');
end;

procedure DrainTransmission(const Pieces: TStrings);
const
  MaximumBlockCount = 10000;
var
  Block: TSingleArray;
  BlockCount: Integer;
  CurrentPiece: string;
begin
  if (Tst.Me.State <> stSending)
    or (Length(Tst.Me.Envelope) = 0)
    or (Tst.Me.MsgText = '') then
    raise Exception.Create('CE did not start a transmission');

  Pieces.Clear;
  CurrentPiece := Tst.Me.MsgText;
  Pieces.Add(CurrentPiece);
  BlockCount := 0;
  while Length(Tst.Me.Envelope) > 0 do
  begin
    Block := Tst.Me.GetBlock;
    Inc(BlockCount);
    if Length(Block) <> Ini.BufSize then
      raise Exception.Create('CE returned an invalid audio block');
    if BlockCount > MaximumBlockCount then
      raise Exception.Create('CE transmission did not drain');

    if (Length(Tst.Me.Envelope) > 0)
      and (Tst.Me.MsgText <> CurrentPiece) then
    begin
      CurrentPiece := Tst.Me.MsgText;
      if CurrentPiece = '' then
        raise Exception.Create('CE queued an empty message piece');
      Pieces.Add(CurrentPiece);
    end;
  end;

  if BlockCount = 0 then
    raise Exception.Create('CE transmission emitted no audio blocks');
end;

procedure AppendMessageToken(
  var Tokens: string;
  const Token: string);
begin
  if Tokens <> '' then
    Tokens := Tokens + ',';
  Tokens := Tokens + Token;
end;

function NormalizeMessagesInSendOrder(
  const InputCall: string;
  const Pieces: TStrings): string;
var
  CqObserved: Boolean;
  ExchangeObserved: Boolean;
  HisCallObserved: Boolean;
  Index: Integer;
  Messages: TStationMessages;
  Piece: string;
  QuestionObserved: Boolean;
  Unexpected: TStationMessages;
begin
  Result := '';
  Messages := Tst.Me.Msg;
  Unexpected := Messages - [msgCQ, msgHisCall, msgNR, msgQm];
  if Unexpected <> [] then
    raise Exception.Create(
      'CE selected an unsupported ESM message: ' + ToStr(Unexpected));

  CqObserved := False;
  ExchangeObserved := False;
  HisCallObserved := False;
  QuestionObserved := False;
  for Index := 0 to Pieces.Count - 1 do
  begin
    Piece := Pieces[Index];
    if (msgHisCall in Messages)
      and not HisCallObserved
      and (Piece = InputCall) then
    begin
      AppendMessageToken(Result, 'his-call:' + Piece);
      HisCallObserved := True;
    end
    else if (msgCQ in Messages) and not CqObserved then
    begin
      AppendMessageToken(Result, 'cq');
      CqObserved := True;
    end
    else if (msgNR in Messages) and not ExchangeObserved then
    begin
      AppendMessageToken(Result, 'exchange');
      ExchangeObserved := True;
    end
    else if (msgQm in Messages)
      and not QuestionObserved
      and (Piece = '?') then
    begin
      AppendMessageToken(Result, 'question');
      QuestionObserved := True;
    end
    else
      raise Exception.Create(
        'CE emitted an unrecognized or duplicate message piece');
  end;

  if CqObserved <> (msgCQ in Messages) then
    raise Exception.Create('CE CQ selection did not match its piece queue');
  if HisCallObserved <> (msgHisCall in Messages) then
    raise Exception.Create(
      'CE entered-call selection did not match its piece queue');
  if ExchangeObserved <> (msgNR in Messages) then
    raise Exception.Create(
      'CE exchange selection did not match its piece queue');
  if QuestionObserved <> (msgQm in Messages) then
    raise Exception.Create(
      'CE question selection did not match its piece queue');
  if Result = '' then
    raise Exception.Create('CE selected no ESM messages');
end;

function NormalizeFocus: string;
begin
  if MainForm.ActiveControl = MainForm.Edit1 then
    Result := 'call'
  else if MainForm.ActiveControl = MainForm.Edit3 then
    Result := 'exchange1'
  else
    raise Exception.Create('CE focused an unsupported entry control');
end;

procedure ObserveQuestionSelection(
  out SelectionStart: Integer;
  out SelectionLength: Integer);
var
  SelectedText: string;
begin
  SelectionStart := -1;
  SelectionLength := 0;
  if MainForm.Edit1.SelLength = 0 then
    Exit;

  SelectedText := Copy(
    MainForm.Edit1.Text,
    MainForm.Edit1.SelStart + 1,
    MainForm.Edit1.SelLength);
  if SelectedText = '?' then
  begin
    SelectionStart := MainForm.Edit1.SelStart;
    SelectionLength := MainForm.Edit1.SelLength;
  end;
end;

function BooleanText(const Value: Boolean): string;
begin
  if Value then
    Result := 'true'
  else
    Result := 'false';
end;

procedure ObserveAction(
  const Values: TStrings;
  const ActionIndex: Integer;
  const Action: TJSONObject);
var
  ActionId: string;
  CallRetained: Boolean;
  InputCall: string;
  Key: Word;
  MessageTokens: string;
  Pieces: TStringList;
  QuestionLength: Integer;
  QuestionStart: Integer;
begin
  ActionId := RequireString(Action, 'id');
  InputCall := RequireString(Action, 'call');
  if Tst.Me.State <> stListening then
    raise Exception.Create('CE runtime was not listening before Enter');

  MainForm.ActiveControl := MainForm.Edit1;
  MainForm.Edit1.Text := InputCall;
  Key := VK_RETURN;
  MainForm.FormKeyDown(MainForm, Key, []);
  if Key <> 0 then
    raise Exception.Create('CE did not consume VK_RETURN');

  Pieces := TStringList.Create;
  try
    DrainTransmission(Pieces);
    MessageTokens := NormalizeMessagesInSendOrder(InputCall, Pieces);
  finally
    Pieces.Free;
  end;

  ObserveQuestionSelection(QuestionStart, QuestionLength);
  CallRetained := MainForm.Edit1.Text = InputCall;
  RequireNoHandles;
  if Tst.Stations.Count <> 0 then
    raise Exception.Create('CE created a remote station during the action');

  Values.Add(
    'action[' + IntToStr(ActionIndex) + ']'
    + '|id=' + ActionId
    + '|input=' + InputCall
    + '|messages=' + MessageTokens
    + '|focus=' + NormalizeFocus
    + '|question-start=' + IntToStr(QuestionStart)
    + '|question-length=' + IntToStr(QuestionLength)
    + '|call=' + MainForm.Edit1.Text
    + '|rst=' + MainForm.Edit2.Text
    + '|exchange1=' + MainForm.Edit3.Text
    + '|call-retained=' + BooleanText(CallRetained)
    + '|qso-count=' + IntToStr(Length(QsoList)));
end;

function RequireContests(const Input: TJSONObject): TJSONArray;
var
  Contest: TJSONObject;
  Data: TJSONData;
  Index: Integer;
begin
  Data := Input.Find('contests');
  if (Data = nil) or (Data.JSONType <> jtArray) then
    raise Exception.Create('contests is not an array');
  Result := TJSONArray(Data);
  if Result.Count <> ExpectedContestCount then
    raise Exception.Create('contests has an unsupported count');

  for Index := 0 to Result.Count - 1 do
  begin
    Data := Result.Items[Index];
    if not (Data is TJSONObject) then
      raise Exception.Create('a contest is not an object');
    Contest := TJSONObject(Data);
    RequireExactObjectFields(
      Contest,
      ['call', 'contestId', 'exchange1', 'exchange2', 'rst', 'runModeId']);
    if RequireString(Contest, 'contestId') <> ExpectedContestIds[Index] then
      raise Exception.Create('contestId does not match the fixed vector');
    if RequireString(Contest, 'runModeId') <> ExpectedRunModeIds[Index] then
      raise Exception.Create('runModeId does not match the fixed vector');
    if RequireString(Contest, 'call') <> ExpectedQsoCalls[Index] then
      raise Exception.Create('QSO call does not match the fixed vector');
    if RequireString(Contest, 'rst') <> ExpectedQsoRsts[Index] then
      raise Exception.Create('QSO RST does not match the fixed vector');
    if RequireString(Contest, 'exchange1') <> ExpectedQsoExchange1[Index] then
      raise Exception.Create('QSO exchange1 does not match the fixed vector');
    if RequireString(Contest, 'exchange2') <> ExpectedQsoExchange2[Index] then
      raise Exception.Create('QSO exchange2 does not match the fixed vector');
  end;
end;

function CaptureMessage(const Message: TStationMessage): string;
begin
  if Tst.Me.State <> stListening then
    raise Exception.Create('CE operator was not listening before message');
  Tst.Me.SendMsg(Message);
  if (Tst.Me.State <> stSending)
    or (Length(Tst.Me.Envelope) = 0)
    or (Tst.Me.MsgText = '')
    or not (Message in Tst.Me.Msg) then
    raise Exception.Create('CE did not start the requested message');
  Result := Tst.Me.MsgText;
end;

procedure CompleteTransmission(const ExpectedText: string);
var
  Pieces: TStringList;
begin
  Pieces := TStringList.Create;
  try
    DrainTransmission(Pieces);
    if (Pieces.Count <> 1) or (Pieces[0] <> ExpectedText) then
      raise Exception.Create('CE emitted an unexpected message queue');
  finally
    Pieces.Free;
  end;
  Tst.Me.Tick;
  if Tst.Me.State <> stListening then
    raise Exception.Create('CE operator did not finish the message');
end;

procedure ObserveDefaultTwoFieldContest(
  const Values: TStrings;
  const ContestOrdinal: Integer;
  const ContestId: string;
  const Exchange2: string;
  const Index: Integer);
var
  FormattedExchange: string;
  StationValue: TOracleStation;
begin
  CreateRuntime(ContestOrdinal);
  StationValue := TOracleStation.CreateStation;
  try
    StationValue.MyCall := ExpectedRemoteCall;
    StationValue.HisCall := ExpectedStationCall;
    StationValue.RST := ExpectedRst;
    StationValue.NR := 0;
    StationValue.Exch1 := IntToStr(ExpectedRst);
    StationValue.Exch2 := Exchange2;
    StationValue.SentExchTypes := Tst.GetSentExchTypes(
      skDxStation,
      ExpectedRemoteCall);
    FormattedExchange := StationValue.ObserveNrAsText;
    Values.Add(
      'contest[' + IntToStr(Index) + ']'
      + '|id=' + ContestId
      + '|call=' + ExpectedRemoteCall
      + '|rst=' + IntToStr(ExpectedRst)
      + '|exchange1=' + IntToStr(ExpectedRst)
      + '|exchange2=' + Exchange2
      + '|formatted=' + FormattedExchange);
  finally
    StationValue.Free;
    DestroyRuntime;
  end;
end;

procedure ObserveDefaultTwoFieldRemoteExchangeFormat(
  const Values: TStrings;
  const Input: TJSONObject);
begin
  RequireExactObjectFields(
    Input,
    [
      'acagExchange2',
      'allJaExchange2',
      'arrlDxExchange2',
      'iaruHfExchange2',
      'remoteCall',
      'rst',
      'runModeId',
      'scenario',
      'seed',
      'stationCall'
    ]);
  if RequireString(Input, 'scenario') <> ExpectedScenario then
    raise Exception.Create('scenario does not match the fixed vector');
  if RequireString(Input, 'stationCall') <> ExpectedStationCall then
    raise Exception.Create('stationCall does not match the fixed vector');
  if RequireString(Input, 'remoteCall') <> ExpectedRemoteCall then
    raise Exception.Create('remoteCall does not match the fixed vector');
  if RequireString(Input, 'runModeId') <> 'rmPileup' then
    raise Exception.Create('runModeId does not match the fixed vector');
  if RequireInteger(Input, 'rst', 0, MaxInt) <> ExpectedRst then
    raise Exception.Create('rst does not match the fixed vector');
  if RequireString(Input, 'arrlDxExchange2') <> ExpectedArrlDxExchange2 then
    raise Exception.Create('arrlDxExchange2 does not match the fixed vector');
  if RequireString(Input, 'allJaExchange2') <> ExpectedAllJaExchange2 then
    raise Exception.Create('allJaExchange2 does not match the fixed vector');
  if RequireString(Input, 'acagExchange2') <> ExpectedAcagExchange2 then
    raise Exception.Create('acagExchange2 does not match the fixed vector');
  if RequireString(Input, 'iaruHfExchange2') <> ExpectedIaruHfExchange2 then
    raise Exception.Create('iaruHfExchange2 does not match the fixed vector');
  if RequireInteger(Input, 'seed', 0, MaxInt) <> ExpectedSeed then
    raise Exception.Create('seed does not match the fixed vector');

  Values.Add(
    'configuration'
    + '|scenario=' + ExpectedScenario
    + '|station=' + ExpectedStationCall
    + '|seed=' + IntToStr(ExpectedSeed)
    + '|run-mode=rmPileup');

  ObserveDefaultTwoFieldContest(
    Values, Ord(scArrlDx), 'scArrlDx', ExpectedArrlDxExchange2, 0);
  ObserveDefaultTwoFieldContest(
    Values, Ord(scAllJa), 'scAllJa', ExpectedAllJaExchange2, 1);
  ObserveDefaultTwoFieldContest(
    Values, Ord(scAcag), 'scAcag', ExpectedAcagExchange2, 2);
  ObserveDefaultTwoFieldContest(
    Values, Ord(scIaruHf), 'scIaruHf', ExpectedIaruHfExchange2, 3);

  if Values.Count <> 5 then
    raise Exception.Create(
      'default two-field exchange scenario emitted invalid rows');
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

    ObserveDefaultTwoFieldRemoteExchangeFormat(Values, Input);
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
