program LegacyOracle;

{$mode Delphi}{$H+}
{$apptype console}

{$ifndef V18_QRM_CALLER_COLLISION}
  {$fatal V18_QRM_CALLER_COLLISION must be defined}
{$endif}

uses
  Interfaces,
  SysUtils,
  Classes,
  TypInfo,
  fpjson,
  jsonparser,
  Ini,
  DXCC,
  Main,
  Contest,
  Station,
  MyStn,
  DxStn,
  DxOper,
  QrmStn,
  MorseKey,
  SndTypes;

const
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v18';
  ExpectedSource =
    'tests/parity/legacy-oracle/v18/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v18/build-recipe.json';
  ExpectedScenario =
    'audio.qrm-caller-collision-retry-limit-seed-24680';
  ExpectedSeed = 24680;
  ExpectedSampleRate = 11025;
  ExpectedBlockSize = 512;
  ExpectedRetryLimit = 10;
  ExpectedCollisionChecks = 9;
  ExpectedAcceptedAttempt = 10;
  ExpectedStationCall = 'W7SST';
  ExpectedCollisionCall = 'K1ABC';
  ExpectedValueCount = 16;
  MaximumTrackedRandomDraws = 100000;
  ExpectedContractRow =
    'contract=ce-live-qrm-caller-collision-v1'
    + '|seed=24680|sample-rate=11025|block-size=512'
    + '|run-mode=rmStop|contest=scWpx|station-call=W7SST'
    + '|qrm=true|qrn=false|qsb=false|flutter=false'
    + '|qsk=false|lids=false';
  ExpectedQrmRow =
    'qrm|class=TQrmStation|state=stSending|call=K1ABC'
    + '|his-call=W7SST|r1-single-bits=3eacff6b'
    + '|amplitude-single-bits=4659f839|pitch-offset-hz=-194'
    + '|wpm-s=44|wpm-c=44|message-set=[msgQrl2]'
    + '|message-text=QRL?   QRL?';
  ExpectedCatalogRow =
    'catalog|pick-station-calls=11|get-call-calls=11'
    + '|get-exchange-calls=10|drop-station-calls=0|qrm-id=9000'
    + '|caller-ids=1,2,3,4,5,6,7,8,9,10|all-calls=K1ABC';
  ExpectedAttempt1Row =
    'attempt[1]|id=1|call=K1ABC|r1-random-ordinal=7'
    + '|r1-single-bits=3e84d1db|wpm-s=30|wpm-c=30'
    + '|skills=1|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt2Row =
    'attempt[2]|id=2|call=K1ABC|r1-random-ordinal=2569'
    + '|r1-single-bits=3f6334d5|wpm-s=30|wpm-c=30'
    + '|skills=2|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt3Row =
    'attempt[3]|id=3|call=K1ABC|r1-random-ordinal=4843'
    + '|r1-single-bits=3dca46ce|wpm-s=30|wpm-c=30'
    + '|skills=2|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt4Row =
    'attempt[4]|id=4|call=K1ABC|r1-random-ordinal=7107'
    + '|r1-single-bits=3f571831|wpm-s=30|wpm-c=30'
    + '|skills=1|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt5Row =
    'attempt[5]|id=5|call=K1ABC|r1-random-ordinal=9633'
    + '|r1-single-bits=3d1c9b48|wpm-s=30|wpm-c=30'
    + '|skills=2|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt6Row =
    'attempt[6]|id=6|call=K1ABC|r1-random-ordinal=12363'
    + '|r1-single-bits=3ee7bb6f|wpm-s=30|wpm-c=30'
    + '|skills=3|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt7Row =
    'attempt[7]|id=7|call=K1ABC|r1-random-ordinal=14739'
    + '|r1-single-bits=3f41ef12|wpm-s=30|wpm-c=30'
    + '|skills=2|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt8Row =
    'attempt[8]|id=8|call=K1ABC|r1-random-ordinal=17619'
    + '|r1-single-bits=3d5256c8|wpm-s=30|wpm-c=30'
    + '|skills=3|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt9Row =
    'attempt[9]|id=9|call=K1ABC|r1-random-ordinal=20333'
    + '|r1-single-bits=3ed9db67|wpm-s=30|wpm-c=30'
    + '|skills=3|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=True|outcome=discarded';
  ExpectedAttempt10Row =
    'attempt[10]|id=10|call=K1ABC|r1-random-ordinal=22841'
    + '|r1-single-bits=3c9570e0|wpm-s=30|wpm-c=30'
    + '|skills=2|patience=5|operator-state=osNeedPrevEnd'
    + '|collision-checked=False|outcome=accepted-unconditionally';
  ExpectedCollisionOutcomeRow =
    'collision-outcome|retry-limit=10'
    + '|checked-attempts=1,2,3,4,5,6,7,8,9'
    + '|discarded-attempts=1,2,3,4,5,6,7,8,9'
    + '|unchecked-attempt=10|accepted-attempt=10|station-count=2'
    + '|duplicate-active-calls=2|qrm-retained=true';
  ExpectedAcceptedCallerRow =
    'accepted-caller|class=TDxStation|call=K1ABC|oper-id=10'
    + '|state=stCopying|operator-state=osNeedPrevEnd'
    + '|operator-patience=5|operator-skills=2'
    + '|r1-single-bits=3c9570e0'
    + '|amplitude-single-bits=46a646f0|pitch-offset-hz=51'
    + '|wpm-s=30|wpm-c=30|rst=599|nr=1010'
    + '|exch1=EX10|exch2=ID10|op-name=OP10'
    + '|user-text=catalog-row-10';
  ExpectedTerminalRandomRow =
    'terminal-random|ordinal=25373|value=0.983176053'
    + '|single-bits=3f7bb16d';

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;

  TAttemptObservation = record
    Id: Integer;
    R1RandomOrdinal: Integer;
    R1: Single;
    WpmS: Integer;
    WpmC: Integer;
    Skills: Integer;
    Patience: Integer;
    OperatorState: TOperatorState;
  end;

  TCollisionContest = class(TContest)
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
  AttemptObservations:
    array[1..ExpectedRetryLimit] of TAttemptObservation;
  GetCallCount: Integer;
  GetExchangeCount: Integer;
  PickStationCount: Integer;
  DropStationCount: Integer;

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

function SingleBits(const Value: Single): string;
var
  Bits: LongWord;
begin
  if SizeOf(Value) <> SizeOf(Bits) then
    raise Exception.Create('CE Single storage is not 32-bit');
  Move(Value, Bits, SizeOf(Bits));
  Result := LowerCase(IntToHex(Bits, 8));
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
  const Name: string): Integer;
var
  Data: TJSONData;
begin
  Data := Input.Find(Name);
  if (Data = nil) or not (Data is TJSONIntegerNumber) then
    raise Exception.Create(Name + ' is not an integer');
  if (Data.AsInt64 < Low(Integer))
    or (Data.AsInt64 > High(Integer)) then
    raise Exception.Create(Name + ' is outside the Int32 range');
  Result := Integer(Data.AsInt64);
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

function TCollisionContest.LoadCallHistory(
  const AUserCallsign: string): Boolean;
begin
  raise Exception.Create('LoadCallHistory must not be called');
  Result := False;
end;

function TCollisionContest.PickStation: Integer;
var
  Attempt: Integer;
begin
  Inc(PickStationCount);
  if PickStationCount = 1 then
    Result := 9000
  else
  begin
    Attempt := PickStationCount - 1;
    if (Attempt < 1) or (Attempt > ExpectedRetryLimit) then
      raise Exception.Create('caller selection exceeded the retry limit');
    Result := Attempt;
    AttemptObservations[Attempt].Id := Result;
  end;
end;

procedure TCollisionContest.DropStation(Id: Integer);
begin
  Inc(DropStationCount);
end;

function TCollisionContest.GetCall(Id: Integer): string;
begin
  Inc(GetCallCount);
  if (GetCallCount = 1) and (Id <> 9000) then
    raise Exception.Create('QRM selection ID changed');
  if (GetCallCount > 1)
    and (Id <> GetCallCount - 1) then
    raise Exception.Create('caller selection ID changed');
  Result := ExpectedCollisionCall;
end;

procedure TCollisionContest.GetExchange(
  Id: Integer;
  out AStation: TDxStation);
var
  Attempt: Integer;
begin
  Inc(GetExchangeCount);
  Attempt := GetExchangeCount;
  if (Attempt < 1)
    or (Attempt > ExpectedRetryLimit)
    or (Id <> Attempt) then
    raise Exception.Create('caller exchange selection changed');

  AttemptObservations[Attempt].R1 := AStation.R1;
  AttemptObservations[Attempt].WpmS := AStation.WpmS;
  AttemptObservations[Attempt].WpmC := AStation.WpmC;
  AttemptObservations[Attempt].Skills := AStation.Oper.Skills;
  AttemptObservations[Attempt].Patience :=
    AStation.Oper.Patience;
  AttemptObservations[Attempt].OperatorState :=
    AStation.Oper.State;

  AStation.RST := 599;
  AStation.NR := 1000 + Attempt;
  AStation.Exch1 := 'EX' + Format('%.2d', [Attempt]);
  AStation.Exch2 := 'ID' + IntToStr(Id);
  AStation.OpName := 'OP' + IntToStr(Attempt);
  AStation.UserText := 'catalog-row-' + IntToStr(Attempt);
end;

procedure ConfigureIni;
begin
  Ini.Call := ExpectedStationCall;
  Ini.Wpm := 30;
  Ini.Bandwidth := 500;
  Ini.Pitch := 600;
  Ini.Qsk := False;
  Ini.Rit := 0;
  Ini.BufSize := ExpectedBlockSize;
  Ini.Activity := 1;
  Ini.Qrn := False;
  Ini.Qrm := True;
  Ini.Qsb := False;
  Ini.Flutter := False;
  Ini.Lids := False;
  Ini.Duration := 30;
  Ini.RunMode := rmStop;
  Ini.SaveWav := False;
  Ini.MinRxWpm := 0;
  Ini.MaxRxWpm := 0;
  Ini.GetWpmUsesGaussian := False;
  Ini.FarnsworthCharRate := Ini.Wpm;
  Ini.AllStationsWpmS := 0;
  Ini.CallsFromKeyer := False;
  Ini.SimContest := scWpx;
  Ini.ActiveContest := @Ini.ContestDefinitions[scWpx];
end;

procedure CreateRuntime;
begin
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('CE runtime globals were not initially clear');

  FillChar(AttemptObservations, SizeOf(AttemptObservations), 0);
  GetCallCount := 0;
  GetExchangeCount := 0;
  PickStationCount := 0;
  DropStationCount := 0;
  ConfigureIni;
  gDXCCList := TDXCC.Create;
  MakeKeyer(ExpectedSampleRate, ExpectedBlockSize);
  Tst := TCollisionContest.Create;

  if (Tst.ClassType <> TCollisionContest)
    or (Tst.Me.ClassType <> TMyStation)
    or (Tst.Stations.Count <> 0)
    or Assigned(MainForm)
    or (Ini.RunMode <> rmStop)
    or not Ini.Qrm
    or Ini.Qrn
    or Ini.Qsb
    or Ini.Flutter
    or Ini.Lids
    or Ini.Qsk then
    raise Exception.Create('CE collision runtime is not pristine');
end;

procedure DestroyRuntime;
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
    raise Exception.Create('CE runtime teardown was incomplete');
end;

function FindRandomOrdinal(
  const Seed: Integer;
  const TargetBits: string): Integer;
var
  Candidate: Single;
begin
  RandSeed := Seed;
  Result := 0;
  while Result < MaximumTrackedRandomDraws do
  begin
    Candidate := Random;
    if SingleBits(Candidate) = TargetBits then
      Exit;
    Inc(Result);
  end;
  raise Exception.Create('random value ordinal was not found');
end;

function AttemptRow(
  const Attempt: Integer): string;
var
  Observation: TAttemptObservation;
begin
  Observation := AttemptObservations[Attempt];
  Result :=
    'attempt[' + IntToStr(Attempt) + ']'
    + '|id=' + IntToStr(Observation.Id)
    + '|call=' + ExpectedCollisionCall
    + '|r1-random-ordinal='
    + IntToStr(Observation.R1RandomOrdinal)
    + '|r1-single-bits=' + SingleBits(Observation.R1)
    + '|wpm-s=' + IntToStr(Observation.WpmS)
    + '|wpm-c=' + IntToStr(Observation.WpmC)
    + '|skills=' + IntToStr(Observation.Skills)
    + '|patience=' + IntToStr(Observation.Patience)
    + '|operator-state='
    + GetEnumName(
        TypeInfo(TOperatorState),
        Ord(Observation.OperatorState))
    + '|collision-checked='
    + BoolToStr(Attempt < ExpectedAcceptedAttempt, True)
    + '|outcome=';
  if Attempt < ExpectedAcceptedAttempt then
    Result := Result + 'discarded'
  else
    Result := Result + 'accepted-unconditionally';
end;

procedure ObserveCollision(
  const Values: TStrings;
  const Input: TJSONObject);
var
  Accepted: TDxStation;
  Attempt: Integer;
  Qrm: TStation;
  TerminalRandomOrdinal: Integer;
  TerminalRandom: Single;
begin
  RequireExactObjectFields(
    Input,
    [
      'acceptedAttempt',
      'blockSize',
      'collisionCall',
      'collisionChecks',
      'retryLimit',
      'sampleRate',
      'scenario',
      'seed',
      'stationCall'
    ]);
  if (RequireInteger(Input, 'acceptedAttempt')
        <> ExpectedAcceptedAttempt)
    or (RequireInteger(Input, 'blockSize') <> ExpectedBlockSize)
    or (RequireString(Input, 'collisionCall')
        <> ExpectedCollisionCall)
    or (RequireInteger(Input, 'collisionChecks')
        <> ExpectedCollisionChecks)
    or (RequireInteger(Input, 'retryLimit') <> ExpectedRetryLimit)
    or (RequireInteger(Input, 'sampleRate') <> ExpectedSampleRate)
    or (RequireString(Input, 'scenario') <> ExpectedScenario)
    or (RequireInteger(Input, 'seed') <> ExpectedSeed)
    or (RequireString(Input, 'stationCall')
        <> ExpectedStationCall) then
    raise Exception.Create(
      'QRM caller collision input does not match its pinned contract');

  CreateRuntime;
  try
    RandSeed := ExpectedSeed;
    Qrm := Tst.Stations.AddQrm;
    if (Qrm.ClassType <> TQrmStation)
      or (Qrm.MyCall <> ExpectedCollisionCall)
      or (Qrm.State <> stSending)
      or (Tst.Stations.Count <> 1) then
      raise Exception.Create('active CE QRM station changed');

    Accepted := TDxStation(Tst.Stations.AddCaller);
    TerminalRandom := Random;

    if (PickStationCount <> ExpectedRetryLimit + 1)
      or (GetCallCount <> ExpectedRetryLimit + 1)
      or (GetExchangeCount <> ExpectedRetryLimit)
      or (DropStationCount <> 0)
      or (Tst.Stations.Count <> 2)
      or (Tst.Stations[0] <> Qrm)
      or (Tst.Stations[1] <> Accepted)
      or (Accepted.ClassType <> TDxStation)
      or (Accepted.MyCall <> Qrm.MyCall)
      or (Accepted.Operid <> ExpectedAcceptedAttempt)
      or (Accepted.NR <> 1000 + ExpectedAcceptedAttempt)
      or (Accepted.Exch1 <> 'EX10')
      or (Accepted.Exch2 <> 'ID10')
      or (Accepted.OpName <> 'OP10')
      or (Accepted.UserText <> 'catalog-row-10')
      or (Accepted.State <> stCopying)
      or (Accepted.Oper.Call <> ExpectedCollisionCall)
      or (Accepted.Oper.State <> osNeedPrevEnd)
      or (Accepted.Oper.Patience <> 5) then
      raise Exception.Create(
        'CE caller collision retry outcome changed');

    for Attempt := 1 to ExpectedRetryLimit do
      AttemptObservations[Attempt].R1RandomOrdinal :=
        FindRandomOrdinal(
          ExpectedSeed,
          SingleBits(AttemptObservations[Attempt].R1));
    TerminalRandomOrdinal := FindRandomOrdinal(
      ExpectedSeed,
      SingleBits(TerminalRandom));

    Values.Add(
      'contract=ce-live-qrm-caller-collision-v1'
      + '|seed=' + IntToStr(ExpectedSeed)
      + '|sample-rate=' + IntToStr(ExpectedSampleRate)
      + '|block-size=' + IntToStr(ExpectedBlockSize)
      + '|run-mode=rmStop'
      + '|contest=scWpx'
      + '|station-call=' + ExpectedStationCall
      + '|qrm=true|qrn=false|qsb=false|flutter=false'
      + '|qsk=false|lids=false');
    Values.Add(
      'qrm'
      + '|class=' + Qrm.ClassName
      + '|state=' + ToStr(Qrm.State)
      + '|call=' + Qrm.MyCall
      + '|his-call=' + Qrm.HisCall
      + '|r1-single-bits=' + SingleBits(Qrm.R1)
      + '|amplitude-single-bits=' + SingleBits(Qrm.Amplitude)
      + '|pitch-offset-hz=' + IntToStr(Qrm.Pitch)
      + '|wpm-s=' + IntToStr(Qrm.WpmS)
      + '|wpm-c=' + IntToStr(Qrm.WpmC)
      + '|message-set=' + ToStr(Qrm.Msg)
      + '|message-text=' + Qrm.MsgText);
    Values.Add(
      'catalog'
      + '|pick-station-calls=' + IntToStr(PickStationCount)
      + '|get-call-calls=' + IntToStr(GetCallCount)
      + '|get-exchange-calls=' + IntToStr(GetExchangeCount)
      + '|drop-station-calls=' + IntToStr(DropStationCount)
      + '|qrm-id=9000'
      + '|caller-ids=1,2,3,4,5,6,7,8,9,10'
      + '|all-calls=' + ExpectedCollisionCall);
    for Attempt := 1 to ExpectedRetryLimit do
      Values.Add(AttemptRow(Attempt));
    Values.Add(
      'collision-outcome'
      + '|retry-limit=' + IntToStr(ExpectedRetryLimit)
      + '|checked-attempts=1,2,3,4,5,6,7,8,9'
      + '|discarded-attempts=1,2,3,4,5,6,7,8,9'
      + '|unchecked-attempt=10'
      + '|accepted-attempt=10'
      + '|station-count=2'
      + '|duplicate-active-calls=2'
      + '|qrm-retained=true');
    Values.Add(
      'accepted-caller'
      + '|class=' + Accepted.ClassName
      + '|call=' + Accepted.MyCall
      + '|oper-id=' + IntToStr(Accepted.Operid)
      + '|state=' + ToStr(Accepted.State)
      + '|operator-state='
      + GetEnumName(
          TypeInfo(TOperatorState),
          Ord(Accepted.Oper.State))
      + '|operator-patience='
      + IntToStr(Accepted.Oper.Patience)
      + '|operator-skills='
      + IntToStr(Accepted.Oper.Skills)
      + '|r1-single-bits=' + SingleBits(Accepted.R1)
      + '|amplitude-single-bits='
      + SingleBits(Accepted.Amplitude)
      + '|pitch-offset-hz=' + IntToStr(Accepted.Pitch)
      + '|wpm-s=' + IntToStr(Accepted.WpmS)
      + '|wpm-c=' + IntToStr(Accepted.WpmC)
      + '|rst=' + IntToStr(Accepted.RST)
      + '|nr=' + IntToStr(Accepted.NR)
      + '|exch1=' + Accepted.Exch1
      + '|exch2=' + Accepted.Exch2
      + '|op-name=' + Accepted.OpName
      + '|user-text=' + Accepted.UserText);
    Values.Add(
      'terminal-random'
      + '|ordinal=' + IntToStr(TerminalRandomOrdinal)
      + '|value=' + FloatValue(TerminalRandom)
      + '|single-bits=' + SingleBits(TerminalRandom));

    if (Values.Count <> ExpectedValueCount)
      or (Values[0] <> ExpectedContractRow)
      or (Values[1] <> ExpectedQrmRow)
      or (Values[2] <> ExpectedCatalogRow)
      or (Values[3] <> ExpectedAttempt1Row)
      or (Values[4] <> ExpectedAttempt2Row)
      or (Values[5] <> ExpectedAttempt3Row)
      or (Values[6] <> ExpectedAttempt4Row)
      or (Values[7] <> ExpectedAttempt5Row)
      or (Values[8] <> ExpectedAttempt6Row)
      or (Values[9] <> ExpectedAttempt7Row)
      or (Values[10] <> ExpectedAttempt8Row)
      or (Values[11] <> ExpectedAttempt9Row)
      or (Values[12] <> ExpectedAttempt10Row)
      or (Values[13] <> ExpectedCollisionOutcomeRow)
      or (Values[14] <> ExpectedAcceptedCallerRow)
      or (Values[15] <> ExpectedTerminalRandomRow) then
      raise Exception.Create(
        'CE QRM caller collision observation changed');
  finally
    DestroyRuntime;
  end;
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
    ObserveCollision(Values, Input);
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
  if ExitStatus <> 0 then
    Halt(ExitStatus);
end.
