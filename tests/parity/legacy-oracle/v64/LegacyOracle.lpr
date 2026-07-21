program LegacyOracle;

{$mode Delphi}

uses
  Interfaces,
  SysUtils,
  Classes,
  fpjson,
  jsonparser,
  TypInfo,
  Ini,
  ExchFields,
  DXCC,
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
  ArrlSS;

const
  ExpectedScenario = 'contest.invalid-own-exchange-messages-ce-order';
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v64';
  ExpectedSource = 'tests/parity/legacy-oracle/v64/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v64/build-recipe.json';

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;

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
    begin
      Result := False;
      Exit;
    end;
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
      + '27ae41e4649b934ca495991b7852b855'
    then
    raise Exception.Create('SHA-256 empty-vector self-test failed');
  if Sha256Bytes('abc') <>
      'ba7816bf8f01cfea414140de5dae2223'
      + 'b00361a396177a9cb410ff61f20015ad'
    then
    raise Exception.Create('SHA-256 abc-vector self-test failed');
end;

function LoadScenarioInput(
  const InputPath: string;
  const Scenario: string;
  const InputSha256: string): TJSONObject;
var
  Data: TJSONData;
  FileStream: TFileStream;
  RawInput: RawByteString;
begin
  if ExtractFileName(InputPath) <> InputSha256 + '.json' then
    raise Exception.Create('Scenario input path is not content addressed');
  if not FileExists(InputPath) then
    raise Exception.Create('Scenario input file does not exist');
  FileStream := TFileStream.Create(InputPath, fmOpenRead or fmShareDenyWrite);
  try
    if FileStream.Size > High(Integer) then
      raise Exception.Create('Scenario input file is too large');
    SetLength(RawInput, Integer(FileStream.Size));
    if Length(RawInput) > 0 then
      FileStream.ReadBuffer(RawInput[1], Length(RawInput));
    if Sha256Bytes(RawInput) <> InputSha256 then
      raise Exception.Create('Scenario input SHA-256 mismatch');
    FileStream.Position := 0;
    Data := GetJSON(FileStream, True);
  finally
    FileStream.Free;
  end;
  if not (Data is TJSONObject) then
  begin
    Data.Free;
    raise Exception.Create('Scenario input root is not an object');
  end;
  Result := TJSONObject(Data);
  try
    if (Result.Count <> 1)
      or (Result.Find('scenario') = nil)
      or (Result.Get('scenario', '') <> Scenario) then
      raise Exception.Create('Scenario input contract mismatch');
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

function CreateContest(const ContestId: TSimContest): TContest;
begin
  case ContestId of
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
      raise Exception.Create('Unsupported contest ID');
  end;
end;

procedure ObserveOneInvalidExchange(
  const Values: TStrings;
  const ContestId: TSimContest);
var
  ContestValue: TContest;
  ErrorText: string;
  IsValid: Boolean;
  Tokens: TStringList;
begin
  Ini.SimContest := ContestId;
  ActiveContest := @ContestDefinitions[ContestId];
  if ActiveContest^.T <> ContestId then
    raise Exception.Create('Active contest selection failed');
  ContestValue := CreateContest(ContestId);
  Tst := ContestValue;
  Tokens := TStringList.Create;
  try
    if not ContestValue.OnSetMyCall('W7SST', ErrorText) then
      raise Exception.Create(
        'Operator callsign selection failed: ' + ErrorText);
    ErrorText := '';
    IsValid := ContestValue.ValidateMyExchange('', Tokens, ErrorText);
    Values.Add(
      'contest|ordinal=' + IntToStr(Ord(ContestId))
      + '|id=' + GetEnumName(TypeInfo(TSimContest), Ord(ContestId))
      + '|valid=' + LowerCase(BoolToStr(IsValid, True))
      + '|error=' + ErrorText);
  finally
    Tokens.Free;
    Tst := nil;
    ContestValue.Free;
  end;
end;

procedure ObserveInvalidExchanges(const Values: TStrings);
var
  ContestId: TSimContest;
begin
  gDXCCList := TDXCC.Create;
  try
    for ContestId := Low(TSimContest) to High(TSimContest) do
      ObserveOneInvalidExchange(Values, ContestId);
  finally
    gDXCCList.Free;
    gDXCCList := nil;
  end;
end;

var
  AdapterId: string;
  BuildRecipe: string;
  BuildRecipeSha256: string;
  CaseDefinitionSha256: string;
  Input: TJSONObject;
  InputPath: string;
  InputSha256: string;
  Scenario: string;
  Source: string;
  SourceSha256: string;
  Values: TStringList;
  VersionId: string;

begin
  if ParamCount <> 11 then
  begin
    WriteLn(
      StdErr,
      'usage: LegacyOracle <legacy-root-with-separator> <scenario> '
      + '<adapter-id> <version-id> <source> <source-sha256> '
      + '<build-recipe> <build-recipe-sha256> '
      + '<case-definition-sha256> <input-sha256> <input-json-path>');
    Halt(2);
  end;

  ValidateSha256Implementation;
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
    raise Exception.Create('Legacy oracle scenario mismatch');
  if AdapterId <> ExpectedAdapterId then
    raise Exception.Create('Legacy oracle adapter ID mismatch');
  if VersionId <> ExpectedVersionId then
    raise Exception.Create('Legacy oracle version ID mismatch');
  if Source <> ExpectedSource then
    raise Exception.Create('Legacy oracle source identity mismatch');
  if BuildRecipe <> ExpectedBuildRecipe then
    raise Exception.Create('Legacy oracle build recipe identity mismatch');
  if not IsSha256(SourceSha256)
    or not IsSha256(BuildRecipeSha256)
    or not IsSha256(CaseDefinitionSha256)
    or not IsSha256(InputSha256) then
    raise Exception.Create('Legacy oracle binding hash is invalid');

  Input := LoadScenarioInput(InputPath, Scenario, InputSha256);
  Values := TStringList.Create;
  try
    ObserveInvalidExchanges(Values);
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
  finally
    Values.Free;
    Input.Free;
  end;
end.
