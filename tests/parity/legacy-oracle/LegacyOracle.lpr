program LegacyOracle;

{$mode Delphi}

uses
  Interfaces,
  SysUtils,
  Classes,
  Math,
  Ini,
  RndFunc,
  Qsb,
  SndTypes,
  MorseKey,
  Mixers,
  QuickAvg,
  WavFile,
  CallsignUtils,
  DXCC,
  SSExchParser,
  ExchFields,
  SerNRGen,
  Station,
  DxOper,
  Contest,
  Log,
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

function JsonEscape(const Value: string): string;
begin
  Result := StringReplace(Value, '\', '\\', [rfReplaceAll]);
  Result := StringReplace(Result, '"', '\"', [rfReplaceAll]);
  Result := StringReplace(Result, #13, '\r', [rfReplaceAll]);
  Result := StringReplace(Result, #10, '\n', [rfReplaceAll]);
end;

function FloatValue(const Value: Double): string;
begin
  Result := FloatToStrF(Value, ffFixed, 18, 9);
end;

procedure Emit(const Scenario: string; const Values: TStrings);
var
  Index: Integer;
begin
  Write('{"scenario":"', JsonEscape(Scenario), '","values":[');
  for Index := 0 to Values.Count - 1 do
  begin
    if Index > 0 then
      Write(',');
    Write('"', JsonEscape(Values[Index]), '"');
  end;
  WriteLn(']}');
end;

procedure AddRandomVector(
  const Values: TStrings;
  const Name: string;
  const Kind: Integer);
var
  Index: Integer;
  Value: Double;
begin
  RandSeed := 12345;
  for Index := 0 to 7 do
  begin
    case Kind of
      0: Value := RndUniform;
      1: Value := RndUShaped;
      2: Value := RndNormal;
      3: Value := RndRayleigh(2.5);
    else
      Value := RndGaussLim(5, 1.5);
    end;
    Values.Add(Format('%s[%d]=%s', [Name, Index, FloatValue(Value)]));
  end;
end;

procedure ObserveEffects(const Values: TStrings);
var
  Index: Integer;
  Samples: TSingleArray;
  Fade: TQsb;
  Sum: Double;
begin
  AddRandomVector(Values, 'uniform', 0);
  AddRandomVector(Values, 'ushaped', 1);
  AddRandomVector(Values, 'normal', 2);
  AddRandomVector(Values, 'rayleigh', 3);
  AddRandomVector(Values, 'gauss-limited', 4);

  RandSeed := 12345;
  for Index := 0 to 7 do
    Values.Add(Format(
      'poisson[%d]=%d',
      [Index, RndPoisson(3.25)]));

  Ini.BufSize := 512;
  Values.Add('seconds-to-blocks=' + IntToStr(SecondsToBlocks(1.25)));
  Values.Add('blocks-to-seconds=' + FloatValue(BlocksToSeconds(12)));

  RandSeed := 12345;
  SetLength(Samples, 512);
  for Index := 0 to High(Samples) do
    Samples[Index] := 1;
  Fade := TQsb.Create;
  try
    Fade.QsbLevel := 0.75;
    Fade.Bandwidth := 0.5;
    Fade.ApplyTo(Samples);
  finally
    Fade.Free;
  end;
  Sum := 0;
  for Index := 0 to High(Samples) do
    Sum := Sum + Samples[Index];
  for Index := 0 to 7 do
    Values.Add(Format(
      'qsb[%d]=%s',
      [Index * 64, FloatValue(Samples[Index * 64])]));
  Values.Add('qsb-sum=' + FloatValue(Sum));
end;

procedure ObserveDsp(const Values: TStrings);
var
  Index: Integer;
  Envelope: TSingleArray;
  Keyer: TKeyer;
  Mixer: TDownMixer;
  Mixed: TComplexArray;
  Input: TSingleArray;
  Average: TQuickAverage;
begin
  Keyer := TKeyer.Create(11025, 512);
  try
    Keyer.SetWpm(30);
    Keyer.MorseMsg := Keyer.Encode('CQ TEST');
    Values.Add('morse=' + Keyer.MorseMsg);
    Envelope := Keyer.Envelope;
    Values.Add('envelope-length=' + IntToStr(Length(Envelope)));
    Values.Add('true-envelope-length=' + IntToStr(Keyer.TrueEnvelopeLen));
    for Index := 0 to 7 do
      Values.Add(Format(
        'envelope[%d]=%s',
        [Index * 32, FloatValue(Envelope[Index * 32])]));
  finally
    Keyer.Free;
  end;

  SetLength(Input, 8);
  for Index := 0 to High(Input) do
    Input[Index] := Index + 1;
  Mixer := TDownMixer.Create;
  try
    Mixer.SamplesPerSec := 8000;
    Mixer.Freq := 1000;
    Mixed := Mixer.Mix(Input);
    for Index := 0 to High(Mixed) do
      Values.Add(Format(
        'downmix[%d]=%s,%s',
        [Index, FloatValue(Mixed[Index].Re), FloatValue(Mixed[Index].Im)]));
  finally
    Mixer.Free;
  end;

  Average := TQuickAverage.Create(nil);
  try
    Average.Points := 4;
    Average.Passes := 2;
    for Index := 0 to 11 do
      Values.Add(Format(
        'quick-average[%d]=%s',
        [Index, FloatValue(Average.Filter(Index + 1))]));
  finally
    Average.Free;
  end;
end;

procedure ObserveParsers(const Values: TStrings);
const
  Calls: array[0..7] of string = (
    'W7SST',
    'F6/W7SST',
    'W7SST/P',
    'RC2FX',
    'KG4AA',
    'KG4ABC',
    'CE9/W7SST',
    'N0CALL'
  );
var
  Index: Integer;
  Dxcc: TDXCC;
  DxccRecord: TDXCCRec;
  MyParser: TMyExchParser;
  SsParser: TSSExchParser;
  ErrorText: string;
  Range: TSerialNRSettings;
  Generator: TSerialNRGen;
begin
  for Index := Low(Calls) to High(Calls) do
    Values.Add(Format(
      'call[%s]=%s|%s|%s',
      [
        Calls[Index],
        ExtractCallsign(Calls[Index]),
        ExtractPrefix(Calls[Index]),
        ExtractPrefix(Calls[Index], False)
      ]));

  Dxcc := TDXCC.Create;
  try
    for Index := Low(Calls) to High(Calls) do
      if Dxcc.FindRec(DxccRecord, Calls[Index]) then
        Values.Add(Format(
          'dxcc[%s]=%s|%s|%s|%s',
          [
            Calls[Index],
            DxccRecord.Entity,
            DxccRecord.Continent,
            DxccRecord.ITU,
            DxccRecord.CQ
          ]))
      else
        Values.Add('dxcc[' + Calls[Index] + ']=not-found');
  finally
    Dxcc.Free;
  end;

  MyParser := TMyExchParser.Create;
  try
    if MyParser.ParseMyExch('123 A 72 OR') then
      Values.Add(
        'my-ss=123 A 72 OR|'
        + MyParser.GroupByName('nr') + '|'
        + MyParser.GroupByName('prec') + '|'
        + MyParser.GroupByName('chk') + '|'
        + MyParser.GroupByName('sect'))
    else
      Values.Add('my-ss-error=' + MyParser.ErrorStr);
  finally
    MyParser.Free;
  end;

  SsParser := TSSExchParser.Create;
  try
    SsParser.ValidateEnteredExchange(
      'W7SST',
      '',
      '123 A W7SST 72 OR',
      ErrorText);
    Values.Add('ss-exchange=' + SsParser.ExchSummary + '|' + ErrorText);
    SsParser.ValidateEnteredExchange(
      'K1ABC',
      '',
      '11 22 33 ID 44 55',
      ErrorText);
    Values.Add('ss-rotation=' + SsParser.ExchSummary + '|' + ErrorText);
  finally
    SsParser.Free;
  end;

  Range.Init('10-20', 10, 20);
  Generator := TSerialNRGen.Create;
  try
    Generator.AddRange(Range);
    RandSeed := 12345;
    for Index := 0 to 7 do
      Values.Add(Format(
        'serial[%d]=%d',
        [Index, Generator.GetNR]));
  finally
    Generator.Free;
  end;

  for Index := Ord(Low(TExchange1Type)) to Ord(High(TExchange1Type)) do
    Values.Add('exchange1-enum[' + IntToStr(Index) + ']='
      + IntToStr(Index));
  for Index := Ord(Low(TExchange2Type)) to Ord(High(TExchange2Type)) do
    Values.Add('exchange2-enum[' + IntToStr(Index) + ']='
      + IntToStr(Index));
end;

procedure ObserveSimulationStates(const Values: TStrings);
var
  Confidence: Integer;
  Operator: TDxOperator;
begin
  RandSeed := 12345;
  Ini.RunMode := rmPileup;
  Ini.Lids := False;
  Operator := TDxOperator.Create('W7SST', osNeedPrevEnd);
  try
    Values.Add(Format(
      'created=%d|skills=%d|patience=%d',
      [Ord(Operator.State), Operator.Skills, Operator.Patience]));
    Operator.MsgReceived([msgCQ]);
    Values.Add(Format(
      'after-cq=%d|patience=%d',
      [Ord(Operator.State), Operator.Patience]));
    Operator.MsgReceived([msgCQ]);
    Values.Add(Format(
      'after-repeat-cq=%d|patience=%d',
      [Ord(Operator.State), Operator.Patience]));
    Operator.SetState(osNeedNr);
    Values.Add(Format(
      'need-nr=%d|patience=%d',
      [Ord(Operator.State), Operator.Patience]));
    Operator.MsgReceived([msgNil]);
    Values.Add('after-nil=' + IntToStr(Ord(Operator.State)));
    Operator.SetState(osNeedQso);
    Values.Add('active-ghosting=' + BoolToStr(Operator.IsGhosting, True));

    Confidence := -1;
    Values.Add(Format(
      'match-full=%d|confidence=%d',
      [
        Ord(Operator.IsMyCall('W7SST', False, @Confidence)),
        Confidence
      ]));
    Values.Add(Format(
      'match-wildcard=%d|confidence=%d',
      [
        Ord(Operator.IsMyCall('W7S??', False, @Confidence)),
        Confidence
      ]));
    Values.Add(Format(
      'match-substring=%d|confidence=%d',
      [
        Ord(Operator.IsMyCall('SST', False, @Confidence)),
        Confidence
      ]));
    Values.Add(Format(
      'match-none=%d|confidence=%d',
      [
        Ord(Operator.IsMyCall('K1ABC', False, @Confidence)),
        Confidence
      ]));
  finally
    Operator.Free;
  end;
end;

procedure ObserveSimulationRuntime(const Values: TStrings);
var
  Index: Integer;
  Operator: TDxOperator;
begin
  RandSeed := 24680;
  Ini.RunMode := rmSingle;
  Ini.Lids := False;
  Operator := TDxOperator.Create('K1ABC', osNeedQso);
  try
    Values.Add('send-delay=' + IntToStr(Operator.GetSendDelay));
    Values.Add('reply-timeout=' + IntToStr(Operator.GetReplyTimeout));
    Operator.MsgReceived([msgCQ]);
    Values.Add(Format(
      'repeat-cq=%d|patience=%d',
      [Ord(Operator.State), Operator.Patience]));
    Operator.SetState(osNeedNr);
    Operator.MsgReceived([msgNR]);
    Values.Add('after-number=' + IntToStr(Ord(Operator.State)));
    Operator.MsgReceived([msgTU]);
    Values.Add('after-tu=' + IntToStr(Ord(Operator.State)));
    Operator.SetState(osNeedQso);
    for Index := 1 to 8 do
    begin
      Operator.MsgReceived([msgNil]);
      Values.Add(Format(
        'timeout-path[%d]=%d|patience=%d',
        [Index, Ord(Operator.State), Operator.Patience]));
      if Operator.State = osFailed then
        Break;
    end;
  finally
    Operator.Free;
  end;
end;

procedure ObserveLogging(const Values: TStrings);
var
  Index: Integer;
  Multipliers: TMultList;
  Qso: TQso;
begin
  Values.Add('score[-1]=' + FormatScore(-1));
  Values.Add('score[0]=' + FormatScore(0));
  Values.Add('score[123]=' + FormatScore(123));
  Values.Add('score[999999]=' + FormatScore(999999));

  Multipliers := TMultList.Create;
  try
    Multipliers.ApplyMults('OR;WA;OR;CA');
    Values.Add('multiplier-count=' + IntToStr(Multipliers.Count));
    for Index := 0 to Multipliers.Count - 1 do
      Values.Add(Format(
        'multiplier[%d]=%s',
        [Index, Multipliers[Index]]));
  finally
    Multipliers.Free;
  end;

  FillChar(Qso, SizeOf(Qso), 0);
  Qso.SetColumnErrorFlag(0);
  Qso.SetColumnErrorFlag(5);
  Qso.SetColumnErrorFlag(31);
  Values.Add('column-flags=' + IntToHex(Qso.ColumnErrorFlags, 8));
  Values.Add('column-0=' + BoolToStr(Qso.TestColumnErrorFlag(0), True));
  Values.Add('column-1=' + BoolToStr(Qso.TestColumnErrorFlag(1), True));
  Values.Add('column-5=' + BoolToStr(Qso.TestColumnErrorFlag(5), True));
  Values.Add('column-31=' + BoolToStr(Qso.TestColumnErrorFlag(31), True));
end;

procedure ObserveAudioAdapter(const Values: TStrings);
var
  FilePath: string;
  FileStream: TFileStream;
  Index: Integer;
  Source: TSingleArray;
  WaveFile: TAlWavFile;
begin
  FilePath := IncludeTrailingPathDelimiter(GetTempDir)
    + 'morse-runner-legacy-oracle.wav';
  if FileExists(FilePath) then
    DeleteFile(FilePath);
  SetLength(Source, 16);
  for Index := 0 to High(Source) do
    Source[Index] := (Index - 8) * 1024;

  WaveFile := TAlWavFile.Create(nil);
  try
    WaveFile.FileName := FilePath;
    WaveFile.SamplesPerSec := 8000;
    WaveFile.BytesPerSample := 2;
    WaveFile.Stereo := False;
    WaveFile.LData := Source;
    WaveFile.OpenWrite;
    WaveFile.Write;
    WaveFile.Close;
    FileStream := TFileStream.Create(FilePath, fmOpenRead);
    try
      Values.Add('written-bytes=' + IntToStr(FileStream.Size));
    finally
      FileStream.Free;
    end;

    WaveFile.OpenRead;
    WaveFile.Read(16);
    Values.Add('sample-count=' + IntToStr(WaveFile.SampleCnt));
    Values.Add('current-sample=' + IntToStr(WaveFile.CurrentSample));
    for Index := 0 to High(WaveFile.LData) do
      Values.Add(Format(
        'sample[%d]=%s',
        [Index, FloatValue(WaveFile.LData[Index])]));
    WaveFile.Close;
  finally
    WaveFile.Free;
    if FileExists(FilePath) then
      DeleteFile(FilePath);
  end;
end;

procedure ObserveOneContest(
  const Values: TStrings;
  const ContestId: TSimContest;
  const ContestValue: TContest);
var
  ErrorText: string;
  ExchangeTypes: TExchTypes;
  Tokens: TStringList;
begin
  Tst := ContestValue;
  Ini.SimContest := ContestId;
  Tokens := TStringList.Create;
  try
    Values.Add(Format(
      'contest[%d].load=%s',
      [Ord(ContestId), BoolToStr(ContestValue.LoadCallHistory('W7SST'), True)]));
    Values.Add(Format(
      'contest[%d].my-call=%s|%s',
      [
        Ord(ContestId),
        BoolToStr(ContestValue.OnSetMyCall('W7SST', ErrorText), True),
        ErrorText
      ]));
    ExchangeTypes := ContestValue.GetExchangeTypes(
      skMyStation,
      mtSendMsg,
      'W7SST',
      'F6ABC');
    Values.Add(Format(
      'contest[%d].sent-types=%d,%d',
      [Ord(ContestId), Ord(ExchangeTypes.Exch1), Ord(ExchangeTypes.Exch2)]));
    ExchangeTypes := ContestValue.GetExchangeTypes(
      skDxStation,
      mtRecvMsg,
      'F6ABC',
      'W7SST');
    Values.Add(Format(
      'contest[%d].recv-types=%d,%d',
      [Ord(ContestId), Ord(ExchangeTypes.Exch1), Ord(ExchangeTypes.Exch2)]));
    Values.Add(Format(
      'contest[%d].my-exchange=%s|%s',
      [
        Ord(ContestId),
        BoolToStr(
          ContestValue.ValidateMyExchange(
            ContestDefinitions[ContestId].ExchDefault,
            Tokens,
            ErrorText),
          True),
        ErrorText
      ]));
    Values.Add(Format(
      'contest[%d].farnsworth=%s',
      [Ord(ContestId), BoolToStr(
        ContestValue.IsFarnsworthAllowed,
        True)]));
  finally
    Tokens.Free;
    Tst := nil;
    ContestValue.Free;
  end;
end;

procedure ObserveContests(const Values: TStrings);
begin
  gDXCCList := TDXCC.Create;
  try
    ObserveOneContest(Values, scWpx, TCqWpx.Create);
    ObserveOneContest(Values, scCwt, TCWOPS.Create);
    ObserveOneContest(Values, scFieldDay, TArrlFieldDay.Create);
    ObserveOneContest(Values, scNaQp, TNcjNaQp.Create);
    ObserveOneContest(Values, scHst, TCqWpx.Create);
    ObserveOneContest(Values, scCQWW, TCqWw.Create);
    ObserveOneContest(Values, scArrlDx, TArrlDx.Create);
    ObserveOneContest(Values, scSst, TCWSST.Create);
    ObserveOneContest(Values, scAllJa, TALLJA.Create);
    ObserveOneContest(Values, scAcag, TACAG.Create);
    ObserveOneContest(Values, scIaruHf, TIaruHf.Create);
    ObserveOneContest(Values, scArrlSS, TSweepstakes.Create);
  finally
    gDXCCList.Free;
    gDXCCList := nil;
  end;
end;

var
  Scenario: string;
  Values: TStringList;

begin
  DefaultFormatSettings.DecimalSeparator := '.';
  if ParamCount < 2 then
  begin
    WriteLn(StdErr, 'usage: LegacyOracle <legacy-root-with-separator> <scenario>');
    Halt(2);
  end;

  Scenario := ParamStr(2);
  Values := TStringList.Create;
  try
    if Scenario = 'simulation.legacy-effects' then
      ObserveEffects(Values)
    else if Scenario = 'audio-dsp.legacy-processing' then
      ObserveDsp(Values)
    else if Scenario = 'data.legacy-parsers' then
      ObserveParsers(Values)
    else if Scenario = 'simulation.state-models' then
      ObserveSimulationStates(Values)
    else if Scenario = 'simulation.runtime-routines' then
      ObserveSimulationRuntime(Values)
    else if Scenario = 'logging.scoring-rate-and-results' then
      ObserveLogging(Values)
    else if Scenario = 'audio.legacy-adapters' then
      ObserveAudioAdapter(Values)
    else if Scenario = 'contest.legacy-implementations' then
      ObserveContests(Values)
    else
    begin
      WriteLn(StdErr, 'unsupported scenario: ', Scenario);
      Halt(3);
    end;
    Emit(Scenario, Values);
  finally
    Values.Free;
  end;
end.
