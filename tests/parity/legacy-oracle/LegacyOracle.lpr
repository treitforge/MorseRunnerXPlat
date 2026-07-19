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
  MovAvg,
  QuickAvg,
  VolumCtl,
  WavFile,
  CallsignUtils,
  DXCC,
  SSExchParser,
  ExchFields,
  SerNRGen,
  Station,
  DxOper,
  DxStn,
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
const
  ReceiverSampleIndexes: array[0..3] of Integer = (0, 128, 256, 511);
var
  Agc: TVolumeControl;
  Index: Integer;
  Block: Integer;
  GlobalSample: Integer;
  Envelope: TSingleArray;
  FilterA: TMovingAverage;
  FilterB: TMovingAverage;
  FilterSwap: TMovingAverage;
  Filtered: TReImArrays;
  Keyer: TKeyer;
  Mixer: TDownMixer;
  Mixed: TComplexArray;
  Input: TSingleArray;
  Modulator: TModulator;
  BlockPeak: Double;
  CarrierCosine: Double;
  CarrierSine: Double;
  NormalizedSample: Double;
  Peak: Double;
  RequestedCarrierCosine: Double;
  RequestedCarrierSine: Double;
  ReceiverInput: TReImArrays;
  ReceiverOutput: TSingleArray;
  SamplePosition: Integer;
  SumSquares: Double;
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

  FilterA := TMovingAverage.Create(nil);
  FilterB := TMovingAverage.Create(nil);
  Modulator := TModulator.Create;
  Agc := TVolumeControl.Create(nil);
  try
    FilterA.Points := Round(0.7 * 11025 / 500);
    FilterA.Passes := 3;
    FilterA.SamplesInInput := 512;
    FilterA.GainDb := 10 * Log10(500 / 500);
    FilterB.Points := FilterA.Points;
    FilterB.Passes := FilterA.Passes;
    FilterB.SamplesInInput := FilterA.SamplesInInput;
    FilterB.GainDb := FilterA.GainDb;

    Modulator.SamplesPerSec := 11025;
    Modulator.CarrierFreq := 600;
    Values.Add(
      'receiver-effective-carrier=' + FloatValue(Modulator.CarrierFreq));

    Agc.NoiseInDb := 76;
    Agc.NoiseOutDb := 76;
    Agc.AttackSamples := 155;
    Agc.HoldSamples := 155;
    Agc.AgcEnabled := True;

    Peak := 0;
    SumSquares := 0;
    CarrierCosine := 0;
    CarrierSine := 0;
    RequestedCarrierCosine := 0;
    RequestedCarrierSine := 0;
    for Block := 0 to 11 do
    begin
      SetLengthReIm(ReceiverInput, 512);
      for Index := 0 to 511 do
      begin
        GlobalSample := Block * 512 + Index;
        ReceiverInput.Re[Index] :=
          9000 * Cos(TWO_PI * 37 * GlobalSample / 11025);
        ReceiverInput.Im[Index] :=
          -9000 * Sin(TWO_PI * 37 * GlobalSample / 11025);
        if GlobalSample < Length(Envelope) then
        begin
          ReceiverInput.Re[Index] := ReceiverInput.Re[Index]
            + 300000 * Envelope[GlobalSample];
          ReceiverInput.Im[Index] := ReceiverInput.Im[Index]
            + 300000 * Envelope[GlobalSample];
        end;
      end;

      FilterB.Filter(ReceiverInput);
      Filtered := FilterA.Filter(ReceiverInput);
      if ((Block + 1) mod 10) = 0 then
      begin
        FilterSwap := FilterA;
        FilterA := FilterB;
        FilterB := FilterSwap;
        FilterB.Reset;
      end;
      ReceiverOutput := Agc.Process(Modulator.Modulate(Filtered));

      BlockPeak := 0;
      for Index := 0 to High(ReceiverOutput) do
      begin
        GlobalSample := Block * 512 + Index;
        NormalizedSample := ReceiverOutput[Index] / 32768;
        Peak := Max(Peak, Abs(NormalizedSample));
        BlockPeak := Max(BlockPeak, Abs(NormalizedSample));
        SumSquares := SumSquares
          + Sqr(NormalizedSample);
        CarrierCosine := CarrierCosine
          + NormalizedSample * Cos(
            TWO_PI * Modulator.CarrierFreq * GlobalSample / 11025);
        CarrierSine := CarrierSine
          + NormalizedSample * Sin(
            TWO_PI * Modulator.CarrierFreq * GlobalSample / 11025);
        RequestedCarrierCosine := RequestedCarrierCosine
          + NormalizedSample * Cos(
            TWO_PI * 600 * GlobalSample / 11025);
        RequestedCarrierSine := RequestedCarrierSine
          + NormalizedSample * Sin(
            TWO_PI * 600 * GlobalSample / 11025);
      end;
      if Block in [0, 5, 11] then
      begin
        Values.Add(Format(
          'receiver-agc-peak[%d]=%s',
          [Block, FloatValue(BlockPeak)]));
        for SamplePosition := Low(ReceiverSampleIndexes)
          to High(ReceiverSampleIndexes) do
        begin
          Index := ReceiverSampleIndexes[SamplePosition];
          Values.Add(Format(
            'receiver[%d,%d]=%s',
            [
              Block,
              Index,
              FloatValue(ReceiverOutput[Index] / 32768)
            ]));
        end;
      end;
    end;
    Values.Add('receiver-peak=' + FloatValue(Peak));
    Values.Add(
      'receiver-active-rms='
      + FloatValue(Sqrt(SumSquares / (12 * 512))));
    Values.Add(
      'receiver-effective-carrier-correlation='
      + FloatValue(
        2 * Sqrt(Sqr(CarrierCosine) + Sqr(CarrierSine))
        / (12 * 512)));
    Values.Add(
      'receiver-requested-carrier-correlation='
      + FloatValue(
        2 * Sqrt(
          Sqr(RequestedCarrierCosine) + Sqr(RequestedCarrierSine))
        / (12 * 512)));
  finally
    Agc.Free;
    Modulator.Free;
    FilterB.Free;
    FilterA.Free;
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

procedure ObserveLiveOperatorSession(const Values: TStrings);
var
  ContestValue: TContest;
  Operator: TDxOperator;
begin
  RandSeed := 24680;
  Ini.RunMode := rmSingle;
  Ini.Lids := False;
  gDXCCList := TDXCC.Create;
  try
    ContestValue := TCqWpx.Create;
    Tst := ContestValue;
    Operator := TDxOperator.Create('K1ABC', osNeedQso);
    try
      Values.Add('joined=' + IntToStr(Ord(Operator.State)));
      Tst.Me.HisCall := Operator.Call;
      Operator.MsgReceived([msgHisCall]);
      Values.Add('after-call=' + IntToStr(Ord(Operator.State)));
      Operator.MsgReceived([msgNR]);
      Values.Add('after-exchange=' + IntToStr(Ord(Operator.State)));
      Operator.MsgReceived([msgTU]);
      Values.Add('after-tu=' + IntToStr(Ord(Operator.State)));
    finally
      Operator.Free;
      Tst := nil;
      ContestValue.Free;
    end;
  finally
    gDXCCList.Free;
    gDXCCList := nil;
  end;
end;

procedure ObserveLiveStationSession(const Values: TStrings);
var
  ContestValue: TContest;
  StationValue: TDxStation;
begin
  RandSeed := 24680;
  Ini.RunMode := rmSingle;
  Ini.Lids := False;
  Ini.Call := 'W7SST';
  Ini.Wpm := 30;
  Ini.BufSize := 512;
  gDXCCList := TDXCC.Create;
  MakeKeyer;
  try
    ContestValue := TCqWpx.Create;
    Tst := ContestValue;
    Tst.LoadCallHistory(Ini.Call);
    StationValue := TDxStation.CreateStation;
    try
      Values.Add(Format(
        'created=%s|station=%d|operator=%d|patience=%d|repeat=%d|nr=%d|rst=%d',
        [
          StationValue.MyCall,
          Ord(StationValue.State),
          Ord(StationValue.Oper.State),
          StationValue.Oper.Patience,
          StationValue.Oper.RepeatCnt,
          StationValue.NR,
          StationValue.RST
        ]));

      Tst.Me.HisCall := StationValue.MyCall;
      StationValue.ProcessEvent(evMeStarted);
      Tst.Me.Msg := [msgHisCall];
      StationValue.ProcessEvent(evMeFinished);
      Values.Add(Format(
        'after-call=%d|operator=%d|patience=%d',
        [
          Ord(StationValue.State),
          Ord(StationValue.Oper.State),
          StationValue.Oper.Patience
        ]));

      StationValue.ProcessEvent(evTimeout);
      Values.Add(Format(
        'number-reply=%d|operator=%d|messages=%s|text=%s',
        [
          Ord(StationValue.State),
          Ord(StationValue.Oper.State),
          ToStr(StationValue.Msg),
          StationValue.MsgText
        ]));

      StationValue.Envelope := nil;
      StationValue.Tick;
      Values.Add(Format(
        'after-number-sent=%d|operator=%d',
        [Ord(StationValue.State), Ord(StationValue.Oper.State)]));

      StationValue.ProcessEvent(evMeStarted);
      Tst.Me.Msg := [msgNR];
      StationValue.ProcessEvent(evMeFinished);
      Values.Add(Format(
        'after-exchange=%d|operator=%d|patience=%d',
        [
          Ord(StationValue.State),
          Ord(StationValue.Oper.State),
          StationValue.Oper.Patience
        ]));

      StationValue.ProcessEvent(evTimeout);
      Values.Add(Format(
        'end-reply=%d|operator=%d|messages=%s|text=%s',
        [
          Ord(StationValue.State),
          Ord(StationValue.Oper.State),
          ToStr(StationValue.Msg),
          StationValue.MsgText
        ]));

      StationValue.Envelope := nil;
      StationValue.Tick;
      StationValue.ProcessEvent(evMeStarted);
      Tst.Me.Msg := [msgTU];
      StationValue.ProcessEvent(evMeFinished);
      Values.Add(Format(
        'after-tu=%d|operator=%d|patience=%d',
        [
          Ord(StationValue.State),
          Ord(StationValue.Oper.State),
          StationValue.Oper.Patience
        ]));
    finally
      StationValue.Free;
      Tst := nil;
      ContestValue.Free;
    end;
  finally
    DestroyKeyer;
    gDXCCList.Free;
    gDXCCList := nil;
  end;
end;

procedure ObserveLogging(const Values: TStrings);
var
  Index: Integer;
  Multipliers: TMultList;
  Qso: TQso;

  function RateAt(
    const ElapsedSeconds: Single;
    const QsoSeconds: array of Single): Integer;
  var
    Count: Integer;
    DurationDays: Single;
    QsoIndex: Integer;
    TimeDays: Single;
  begin
    if ElapsedSeconds = 0 then
      Exit(0);
    TimeDays := ElapsedSeconds / 86400;
    DurationDays := Min(5 / 1440, TimeDays);
    Count := 0;
    for QsoIndex := High(QsoSeconds) downto Low(QsoSeconds) do
      if (QsoSeconds[QsoIndex] / 86400) >
          (TimeDays - DurationDays) then
        Inc(Count)
      else
        Break;
    Result := Round(Count / DurationDays / 24);
  end;
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
  Values.Add('rate[0]=' + IntToStr(RateAt(0, [])));
  Values.Add('rate[60]=' + IntToStr(RateAt(60, [10, 30, 59])));
  Values.Add(
    'rate[600]=' + IntToStr(RateAt(600, [100, 300, 301, 599])));
  Values.Add('rate[600-empty]=' + IntToStr(RateAt(600, [])));
end;

procedure ObserveCqWpxScoring(const Values: TStrings);
const
  Calls: array[0..4] of string = (
    'K1ABC',
    'K2XYZ',
    'K1ABC',
    'DL2XYZ',
    'F6/W7SST'
  );
var
  CallIndex: Integer;
  PreviousIndex: Integer;
  Qso: TQso;
  ContestValue: TCqWpx;
  WorkedCalls: TStringList;
  Multipliers: TMultList;
  VerifiedPoints: Integer;
  IsDuplicate: Boolean;
  ErrorText: string;
begin
  gDXCCList := TDXCC.Create;
  ContestValue := TCqWpx.Create;
  WorkedCalls := TStringList.Create;
  Multipliers := TMultList.Create;
  VerifiedPoints := 0;
  try
    ErrorText := '';
    Values.Add(
      'call[AB]='
      + BoolToStr(ContestValue.CheckEnteredCallLength('AB', ErrorText), True)
      + '|'
      + ErrorText);
    ErrorText := '';
    Values.Add(
      'call[K1ABC]='
      + BoolToStr(
          ContestValue.CheckEnteredCallLength('K1ABC', ErrorText),
          True)
      + '|'
      + ErrorText);

    for CallIndex := Low(Calls) to High(Calls) do
    begin
      FillChar(Qso, SizeOf(Qso), 0);
      Qso.Call := Calls[CallIndex];
      Qso.Pfx := ExtractPrefix(Qso.Call);
      Qso.MultStr := ContestValue.ExtractMultiplier(@Qso);
      IsDuplicate := False;
      for PreviousIndex := 0 to WorkedCalls.Count - 1 do
        if WorkedCalls[PreviousIndex] = Qso.Call then
          IsDuplicate := True;
      Qso.Dupe := IsDuplicate;

      if not Qso.Dupe then
      begin
        Inc(VerifiedPoints, Qso.Points);
        Multipliers.ApplyMults(Qso.MultStr);
      end;

      Values.Add(Format(
        'qso[%d]=%s|%s|%s|%d|%s|%d|%d|%d',
        [
          CallIndex,
          Qso.Call,
          Qso.Pfx,
          Qso.MultStr,
          Qso.Points,
          BoolToStr(Qso.Dupe, True),
          VerifiedPoints,
          Multipliers.Count,
          VerifiedPoints * Multipliers.Count
        ]));
      WorkedCalls.Add(Qso.Call);
    end;
  finally
    Multipliers.Free;
    WorkedCalls.Free;
    ContestValue.Free;
    gDXCCList.Free;
    gDXCCList := nil;
  end;
end;

procedure ObserveCwtScoring(const Values: TStrings);
const
  Calls: array[0..4] of string = (
    'K1ABC',
    'K2XYZ',
    'K1ABC',
    'K2XYZ/P',
    'K2XYZ/P'
  );
var
  CallIndex: Integer;
  PreviousIndex: Integer;
  Qso: TQso;
  ContestValue: TCWOPS;
  WorkedCalls: TStringList;
  Multipliers: TMultList;
  VerifiedPoints: Integer;
  IsDuplicate: Boolean;
  ErrorText: string;
begin
  gDXCCList := TDXCC.Create;
  ContestValue := TCWOPS.Create;
  WorkedCalls := TStringList.Create;
  Multipliers := TMultList.Create;
  VerifiedPoints := 0;
  try
    ErrorText := '';
    Values.Add(
      'call[AB]='
      + BoolToStr(ContestValue.CheckEnteredCallLength('AB', ErrorText), True)
      + '|'
      + ErrorText);
    ErrorText := '';
    Values.Add(
      'call[K1ABC]='
      + BoolToStr(
          ContestValue.CheckEnteredCallLength('K1ABC', ErrorText),
          True)
      + '|'
      + ErrorText);
    Values.Add('member[123]=' + BoolToStr(IsNum('123'), True));
    Values.Add('member[OR]=' + BoolToStr(IsNum('OR'), True));
    Values.Add('member[]=' + BoolToStr(IsNum(''), True));

    for CallIndex := Low(Calls) to High(Calls) do
    begin
      FillChar(Qso, SizeOf(Qso), 0);
      Qso.Call := Calls[CallIndex];
      Qso.Pfx := ExtractPrefix(Qso.Call);
      Qso.MultStr := ContestValue.ExtractMultiplier(@Qso);
      IsDuplicate := False;
      for PreviousIndex := 0 to WorkedCalls.Count - 1 do
        if WorkedCalls[PreviousIndex] = Qso.Call then
          IsDuplicate := True;
      Qso.Dupe := IsDuplicate;

      if not Qso.Dupe then
      begin
        Inc(VerifiedPoints, Qso.Points);
        Multipliers.ApplyMults(Qso.MultStr);
      end;

      Values.Add(Format(
        'qso[%d]=%s|%s|%d|%s|%d|%d|%d',
        [
          CallIndex,
          Qso.Call,
          Qso.MultStr,
          Qso.Points,
          BoolToStr(Qso.Dupe, True),
          VerifiedPoints,
          Multipliers.Count,
          VerifiedPoints * Multipliers.Count
        ]));
      WorkedCalls.Add(Qso.Call);
    end;
  finally
    Multipliers.Free;
    WorkedCalls.Free;
    ContestValue.Free;
    gDXCCList.Free;
    gDXCCList := nil;
  end;
end;

procedure AddExchangeValidation(
  const Values: TStrings;
  const Name: string;
  const ContestId: TSimContest;
  const ContestValue: TContest;
  const ValidExchange: string);
var
  ErrorText: string;
  Tokens: TStringList;
begin
  Ini.SimContest := ContestId;
  ActiveContest := @ContestDefinitions[ContestId];
  Tst := ContestValue;
  Tokens := TStringList.Create;
  try
    ContestValue.OnSetMyCall('W7SST', ErrorText);
    ErrorText := '';
    Values.Add(
      Name + '.valid='
      + BoolToStr(
          ContestValue.ValidateMyExchange(
            ValidExchange,
            Tokens,
            ErrorText),
          True)
      + '|'
      + ErrorText);
    ErrorText := '';
    Values.Add(
      Name + '.invalid='
      + BoolToStr(
          ContestValue.ValidateMyExchange('', Tokens, ErrorText),
          True)
      + '|'
      + ErrorText);
  finally
    Tokens.Free;
  end;
end;

procedure AddScoredQso(
  const Values: TStrings;
  const Name: string;
  const Index: Integer;
  const ContestValue: TContest;
  const WorkedCalls: TStringList;
  const Multipliers: TMultList;
  var VerifiedPoints: Integer;
  const Call: string;
  const Exchange1: string;
  const Exchange2: string;
  const TrueExchange2: string = '';
  const Section: string = '');
var
  PreviousIndex: Integer;
  Qso: TQso;
  IsDuplicate: Boolean;
begin
  FillChar(Qso, SizeOf(Qso), 0);
  Qso.Call := Call;
  Qso.Rst := 599;
  Qso.Exch1 := Exchange1;
  Qso.Exch2 := Exchange2;
  Qso.TrueExch2 := TrueExchange2;
  Qso.Nr := StrToIntDef(Exchange2, 0);
  Qso.Sect := Section;
  Qso.Pfx := ExtractPrefix(Call);
  Qso.MultStr := ContestValue.ExtractMultiplier(@Qso);
  IsDuplicate := False;
  for PreviousIndex := 0 to WorkedCalls.Count - 1 do
    if WorkedCalls[PreviousIndex] = Qso.Call then
      IsDuplicate := True;
  Qso.Dupe := IsDuplicate;

  if not Qso.Dupe then
  begin
    Inc(VerifiedPoints, Qso.Points);
    Multipliers.ApplyMults(Qso.MultStr);
  end;

  Values.Add(Format(
    '%s.qso[%d]=%s|%s|%d|%s|%d|%d|%d',
    [
      Name,
      Index,
      Qso.Call,
      Qso.MultStr,
      Qso.Points,
      BoolToStr(Qso.Dupe, True),
      VerifiedPoints,
      Multipliers.Count,
      VerifiedPoints * Multipliers.Count
    ]));
  WorkedCalls.Add(Qso.Call);
end;

function LegacyCallToScore(const CallValue: string): Integer;
var
  Encoded: string;
  Index: Integer;
begin
  Encoded := Keyer.Encode(CallValue);
  Result := -1;
  for Index := 1 to Length(Encoded) do
    case Encoded[Index] of
      '.': Inc(Result, 2);
      '-': Inc(Result, 4);
      ' ': Inc(Result, 2);
    end;
end;

procedure ObserveRemainingContestScoring(const Values: TStrings);
var
  ContestValue: TContest;
  ErrorText: string;
  HstScore: Integer;
  Index: Integer;
  Multipliers: TMultList;
  ScoreValue: Integer;
  WorkedCalls: TStringList;
begin
  gDXCCList := TDXCC.Create;
  WorkedCalls := TStringList.Create;
  Multipliers := TMultList.Create;
  try
    ContestValue := TArrlFieldDay.Create;
    try
      AddExchangeValidation(
        Values, 'field-day', scFieldDay, ContestValue, '3A OR');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'field-day', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K1ABC', '3A', 'OR');
      AddScoredQso(
        Values, 'field-day', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K2XYZ', '1D', 'EMA');
      AddScoredQso(
        Values, 'field-day', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K1ABC', '3A', 'OR');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TNcjNaQp.Create;
    try
      AddExchangeValidation(
        Values, 'naqp', scNaQp, ContestValue, 'ALEX ON');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'naqp', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1ABC', 'ALEX', 'MA');
      AddScoredQso(
        Values, 'naqp', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'VE3ABC', 'PAT', 'ON');
      AddScoredQso(
        Values, 'naqp', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', 'HANS', 'DX');
      AddScoredQso(
        Values, 'naqp', 3, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1ABC', 'ALEX', 'MA');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    ContestValue := TCqWpx.Create;
    try
      AddExchangeValidation(
        Values, 'hst', scHst, ContestValue, '5NN 123');
      MakeKeyer;
      try
        WorkedCalls.Clear;
        HstScore := 0;
        for Index := 0 to 3 do
        begin
          case Index of
            0: ErrorText := 'E';
            1: ErrorText := 'T';
            2, 3: ErrorText := 'K1ABC';
          end;
          ScoreValue := LegacyCallToScore(ErrorText);
          if WorkedCalls.IndexOf(ErrorText) < 0 then
            Inc(HstScore, ScoreValue);
          Values.Add(Format(
            'hst.qso[%d]=%s|%d|%s|%d',
            [
              Index,
              ErrorText,
              ScoreValue,
              BoolToStr(WorkedCalls.IndexOf(ErrorText) >= 0, True),
              HstScore
            ]));
          WorkedCalls.Add(ErrorText);
        end;
      finally
        DestroyKeyer;
      end;
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TCqWw.Create;
    try
      AddExchangeValidation(
        Values, 'cqww', scCQWW, ContestValue, '5NN 3');
      ContestValue.LoadCallHistory('W7SST');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'cqww', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1ABC', '599', '3');
      AddScoredQso(
        Values, 'cqww', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'VE3ABC', '599', '4');
      AddScoredQso(
        Values, 'cqww', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', '599', '14');
      AddScoredQso(
        Values, 'cqww', 3, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K1ABC/MM', '599', '5');
      AddScoredQso(
        Values, 'cqww', 4, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', '599', '14');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TArrlDx.Create;
    try
      AddExchangeValidation(
        Values, 'arrl-dx', scArrlDx, ContestValue, '5NN ON');
      ContestValue.OnSetMyCall('W7SST', ErrorText);
      ScoreValue := 0;
      AddScoredQso(
        Values, 'arrl-dx', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', '599', 'KW');
      AddScoredQso(
        Values, 'arrl-dx', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'F6ABC', '599', '100');
      AddScoredQso(
        Values, 'arrl-dx', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', '599', 'KW');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TCWSST.Create;
    try
      AddExchangeValidation(
        Values, 'sst', scSst, ContestValue, 'BRUCE MA');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'sst', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1ABC', 'BRUCE', 'MA', 'MA');
      AddScoredQso(
        Values, 'sst', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'VE3ABC', 'PAT', 'ON', 'ON');
      AddScoredQso(
        Values, 'sst', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', 'HANS', 'DX', 'DX');
      AddScoredQso(
        Values, 'sst', 3, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1ABC', 'BRUCE', 'MA', 'MA');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TALLJA.Create;
    try
      AddExchangeValidation(
        Values, 'all-ja', scAllJa, ContestValue, '5NN 10H');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'all-ja', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'JA1ABC', '599', '10H');
      AddScoredQso(
        Values, 'all-ja', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'JA2XYZ', '599', '101M');
      AddScoredQso(
        Values, 'all-ja', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'JA1ABC', '599', '10H');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TACAG.Create;
    try
      AddExchangeValidation(
        Values, 'acag', scAcag, ContestValue, '5NN 1002H');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'acag', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'JA1ABC', '599', '1002H');
      AddScoredQso(
        Values, 'acag', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'JA2XYZ', '599', '01001M');
      AddScoredQso(
        Values, 'acag', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'JA1ABC', '599', '1002H');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TIaruHf.Create;
    try
      AddExchangeValidation(
        Values, 'iaru-hf', scIaruHf, ContestValue, '5NN 6');
      ContestValue.OnSetMyCall('W7SST', ErrorText);
      ContestValue.Me.Exch2 := '6';
      ScoreValue := 0;
      AddScoredQso(
        Values, 'iaru-hf', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1ABC', '599', '6');
      AddScoredQso(
        Values, 'iaru-hf', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'VE3ABC', '599', '9');
      AddScoredQso(
        Values, 'iaru-hf', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', '599', '28');
      AddScoredQso(
        Values, 'iaru-hf', 3, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'W1AW', '599', 'ARRL');
      AddScoredQso(
        Values, 'iaru-hf', 4, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'DL1ABC', '599', '28');
    finally
      Tst := nil;
      ContestValue.Free;
    end;

    WorkedCalls.Clear;
    Multipliers.Clear;
    ContestValue := TSweepstakes.Create;
    try
      AddExchangeValidation(
        Values, 'arrl-ss', scArrlSS, ContestValue, 'A 72 OR');
      ScoreValue := 0;
      AddScoredQso(
        Values, 'arrl-ss', 0, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K1ABC', '123 A', '72 OR', '', 'OR');
      AddScoredQso(
        Values, 'arrl-ss', 1, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K2XYZ', '124 B', '80 ID', '', 'ID');
      AddScoredQso(
        Values, 'arrl-ss', 2, ContestValue, WorkedCalls,
        Multipliers, ScoreValue, 'K1ABC', '125 A', '72 OR', '', 'OR');
    finally
      Tst := nil;
      ContestValue.Free;
    end;
  finally
    Tst := nil;
    ActiveContest := @ContestDefinitions[scWpx];
    Multipliers.Free;
    WorkedCalls.Free;
    gDXCCList.Free;
    gDXCCList := nil;
  end;
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
    else if Scenario = 'simulation.live-operator-session' then
      ObserveLiveOperatorSession(Values)
    else if Scenario = 'simulation.live-station-session' then
      ObserveLiveStationSession(Values)
    else if Scenario = 'logging.scoring-rate-and-results' then
      ObserveLogging(Values)
    else if Scenario = 'contest.cq-wpx-scoring' then
      ObserveCqWpxScoring(Values)
    else if Scenario = 'contest.cwt-scoring' then
      ObserveCwtScoring(Values)
    else if Scenario = 'contest.remaining-scoring' then
      ObserveRemainingContestScoring(Values)
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
