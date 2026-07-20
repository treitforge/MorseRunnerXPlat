program LegacyOracle;

{$mode Delphi}{$H+}
{$apptype console}

uses
  Interfaces,
  SysUtils,
  Classes,
  fpjson,
  jsonparser,
  Ini,
  DXCC,
  Main,
  Contest,
  Station,
  MyStn,
  CWSST,
  MorseKey,
  FarnsKeyer,
  SndTypes;

const
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v4';
  ExpectedSource =
    'tests/parity/legacy-oracle/v4/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v4/build-recipe.json';
  ExpectedScenario = 'audio.sst-farnsworth-envelope-timing';
  ExpectedSampleRate = 11025;
  ExpectedBlockSize = 512;
  ExpectedAmplitude = 300000;
  ExpectedSendingWpm = 15;
  ExpectedCharacterWpm = 25;
  ExpectedMessageCount = 2;

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;
  TStringArray = array of string;

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

function FloatBlockSha256(const Data: TSingleArray): string;
var
  Raw: RawByteString;
begin
  SetLength(Raw, Length(Data) * SizeOf(Single));
  if Length(Raw) > 0 then
    Move(Data[0], Raw[1], Length(Raw));
  Result := Sha256Bytes(Raw);
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

function RequireStringArray(
  const Input: TJSONObject;
  const Name: string): TStringArray;
var
  ArrayData: TJSONArray;
  Data: TJSONData;
  Index: Integer;
begin
  Data := Input.Find(Name);
  if (Data = nil) or (Data.JSONType <> jtArray) then
    raise Exception.Create(Name + ' is not an array');
  ArrayData := TJSONArray(Data);
  if ArrayData.Count = 0 then
    raise Exception.Create(Name + ' is empty');
  SetLength(Result, ArrayData.Count);
  for Index := 0 to ArrayData.Count - 1 do
  begin
    Data := ArrayData.Items[Index];
    if (Data.JSONType <> jtString) or (Trim(Data.AsString) = '') then
      raise Exception.Create(Name + ' contains an invalid string');
    Result[Index] := Data.AsString;
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

procedure ConfigureIni;
begin
  Ini.Call := 'VE3NEA';
  Ini.Wpm := ExpectedSendingWpm;
  Ini.Bandwidth := 550;
  Ini.Pitch := 450;
  Ini.Qsk := False;
  Ini.Rit := 0;
  Ini.BufSize := ExpectedBlockSize;
  Ini.Activity := 2;
  Ini.Qrn := False;
  Ini.Qrm := False;
  Ini.Qsb := False;
  Ini.Flutter := False;
  Ini.Duration := 30;
  Ini.RunMode := rmPileup;
  Ini.SaveWav := False;
  Ini.FarnsworthCharRate := ExpectedCharacterWpm;
  Ini.AllStationsWpmS := 0;
  Ini.CallsFromKeyer := False;
  Ini.SimContest := scSst;
  Ini.ActiveContest := @Ini.ContestDefinitions[scSst];
end;

procedure CreateSstRuntime(
  const SampleRate: Integer;
  const BlockSize: Integer;
  const Amplitude: Integer;
  const SendingWpm: Integer;
  const CharacterWpm: Integer);
begin
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime globals were not initially clear');

  ConfigureIni;
  Ini.FarnsworthCharRate := CharacterWpm;
  RandSeed := 12345;
  gDXCCList := TDXCC.Create;
  Keyer := TFarnsKeyer.Create(SampleRate, BlockSize);
  Tst := TCWSST.Create;
  Tst.Me.Amplitude := Amplitude;
  Tst.Me.SetWpm(SendingWpm);

  if (Tst.ClassType <> TCWSST)
    or (Tst.Me.ClassType <> TMyStation)
    or (Keyer.ClassType <> TFarnsKeyer)
    or not Tst.IsFarnsworthAllowed then
    raise Exception.Create('CE SST runtime class path is not active');
  if (Tst.Me.WpmS <> SendingWpm)
    or (Tst.Me.WpmC <> CharacterWpm)
    or (Tst.Me.Amplitude <> Amplitude) then
    raise Exception.Create('CE SST runtime timing is not configured');
  if Tst.Stations.Count <> 0 then
    raise Exception.Create('CE SST runtime has unexpected remote stations');
  if Assigned(MainForm) then
    raise Exception.Create('CE SST runtime unexpectedly created a GUI');
end;

procedure DestroySstRuntime;
begin
  if Assigned(Tst) then
    FreeAndNil(Tst);
  DestroyKeyer;
  if Assigned(gDXCCList) then
    FreeAndNil(gDXCCList);
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE SST runtime teardown was incomplete');
end;

procedure ObserveSst(
  const Values: TStrings;
  const Input: TJSONObject);
var
  Amplitude: Integer;
  BlockSize: Integer;
  CharacterWpm: Integer;
  Index: Integer;
  Messages: TStringArray;
  SampleRate: Integer;
  SendingWpm: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'amplitude',
      'blockSize',
      'characterWpm',
      'messages',
      'sampleRate',
      'scenario',
      'sendingWpm'
    ]);
  SampleRate := RequireInteger(Input, 'sampleRate', 1, MaxInt);
  BlockSize := RequireInteger(Input, 'blockSize', 1, MaxInt);
  Amplitude := RequireInteger(Input, 'amplitude', 1, MaxInt);
  SendingWpm := RequireInteger(Input, 'sendingWpm', 1, MaxInt);
  CharacterWpm := RequireInteger(Input, 'characterWpm', 1, MaxInt);
  Messages := RequireStringArray(Input, 'messages');

  if (SampleRate <> ExpectedSampleRate)
    or (BlockSize <> ExpectedBlockSize)
    or (Amplitude <> ExpectedAmplitude)
    or (SendingWpm <> ExpectedSendingWpm)
    or (CharacterWpm <> ExpectedCharacterWpm)
    or (Length(Messages) <> ExpectedMessageCount)
    or (Messages[0] <> 'PARIS TEST')
    or (Messages[1] <> 'K1ABC 599 123') then
    raise Exception.Create('SST scenario input does not match its contract');

  Values.Add(
    'configuration'
    + '|sample-rate=' + IntToStr(SampleRate)
    + '|block-size=' + IntToStr(BlockSize)
    + '|amplitude=' + IntToStr(Amplitude)
    + '|sending-wpm=' + IntToStr(SendingWpm)
    + '|character-wpm=' + IntToStr(CharacterWpm));

  for Index := 0 to High(Messages) do
  begin
    CreateSstRuntime(
      SampleRate,
      BlockSize,
      Amplitude,
      SendingWpm,
      CharacterWpm);
    try
      Tst.SendText(Tst.Me, Messages[Index]);
      if (Tst.Me.State <> stSending)
        or (Tst.Me.MsgText <> Messages[Index])
        or (Length(Tst.Me.Envelope) = 0)
        or (Keyer.TrueEnvelopeLen <= 0)
        or (Keyer.TrueEnvelopeLen > Length(Tst.Me.Envelope))
        or ((Length(Tst.Me.Envelope) mod BlockSize) <> 0) then
        raise Exception.Create('CE SST send path produced invalid state');

      Values.Add(
        'message[' + IntToStr(Index) + ']=' + Messages[Index]);
      Values.Add(
        'timing[' + IntToStr(Index) + ']'
        + '|sending-wpm=' + IntToStr(Tst.Me.WpmS)
        + '|character-wpm=' + IntToStr(Tst.Me.WpmC)
        + '|amplitude=' + IntToStr(Round(Tst.Me.Amplitude)));
      Values.Add(
        'true-length[' + IntToStr(Index) + ']='
        + IntToStr(Keyer.TrueEnvelopeLen));
      Values.Add(
        'padded-length[' + IntToStr(Index) + ']='
        + IntToStr(Length(Tst.Me.Envelope)));
      Values.Add(
        'float-sha256[' + IntToStr(Index) + ']='
        + FloatBlockSha256(Tst.Me.Envelope));
    finally
      DestroySstRuntime;
    end;
  end;

  if Values.Count <> 11 then
    raise Exception.Create('SST scenario emitted an invalid row count');
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
    ObserveSst(Values, Input);
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
      DestroySstRuntime;
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
  if ExitStatus <> 0 then
    Halt(ExitStatus);
end.
