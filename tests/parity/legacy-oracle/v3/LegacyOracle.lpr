program LegacyOracle;

{$mode Delphi}{$H+}
{$apptype console}

uses
{$IFDEF V3_NOGUI}
  InterfaceBase,
  NoGUIInt,
  NoGUIWSFactory,
{$ELSE}
  Interfaces,
{$ENDIF}
  Forms,
  LCLType,
  Types,
  Controls,
  ExtCtrls,
  StdCtrls,
  Spin,
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
  Math,
  Main,
  Contest,
  Station,
  StnColl,
  MyStn,
  DxStn,
  CWSST,
  Ini,
  DXCC,
  MorseKey,
  FarnsKeyer,
  SndTypes,
  SndOut,
  WavFile,
  VolmSldr;

const
  ExpectedAdapterId = 'LegacyOracleTarget';
  ExpectedVersionId = 'legacy-oracle-v3';
  ExpectedSource =
    'tests/parity/legacy-oracle/v3/LegacyOracle.lpr';
  ExpectedBuildRecipe =
    'tests/parity/legacy-oracle/v3/build-recipe.json';
  ExpectedCaseDefinition =
    'tests/parity/legacy-oracle/v3/case-contracts.json';
  ExpectedAdapterDescriptor =
    'tests/parity/legacy-oracle/v3/adapter-descriptor.json';
  ExpectedLegacyReferenceDefinition =
    'tests/parity/legacy-reference.json';
  ExpectedLegacyReferenceDefinitionSha256 =
    '663adf3bf230161abb923cf8b6651d394'
    + 'af1b99eab05efeafb696cb29992da23';
  ExpectedLegacyBundle =
    'tests/parity/legacy-reference.bundle';
  ExpectedLegacyBundleSha256 =
    '1d9fcafb3adb0227aba360bc1884b5c32'
    + 'd2c1e8210448e646a4104f142b07772';
  ExpectedLegacyRevision =
    '55bbd019c29d8cf693184ea420a17a253f16fe1e';
  ExpectedLegacyTree =
    'a44212bfee5b1eebfd0129459d476736775adf36';
  ExpectedDxccListSha256 =
    '94ad79465eb8cd8df91861f5cd8d6706'
    + '4f8c3ba39a9774a737eb5f53d0b51049';
  ExpectedZeroSingleSha256 =
    'df3f619804a92fdb4057192dc43dd748e'
    + 'a778adc52bc498ce80524c014b81119';

type
  TSha256State = array[0..7] of LongWord;
  TSha256Schedule = array[0..63] of LongWord;
  TByteBuffer = array of Byte;
  TStringArray = array of string;
  TIntegerArray = array of Integer;

  TReceiverControl = record
    Block: Integer;
    RitHz: Integer;
    BandwidthHz: Integer;
  end;

  TReceiverControlArray = array of TReceiverControl;

  TAudioBlockObservation = record
    RequestNumber: Integer;
    AbsoluteBlock: Integer;
    SampleCount: Integer;
    Peak: Double;
    Rms: Double;
    FloatSha256: string;
  end;

  TAudioBlockObservations = array of TAudioBlockObservation;

{$IFDEF V3_NOGUI}
  TCandidateNoGuiWidgetSet = class(TNoGUIWidgetSet)
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
{$ENDIF}

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

  TFixedStation = class(TStation)
  public
    constructor CreateFixed(
      ACollection: TCollection;
      const APitchHz: Integer;
      const AAmplitude: Single;
      const ABlockCount: Integer);
    procedure ProcessEvent(AEvent: TStationEvent); override;
  end;

var
  AbstractStubCalls: Integer;
  ProcessEventStubCalls: Integer;

{$IFDEF V3_NOGUI}
function TCandidateNoGuiWidgetSet.EnumDisplayMonitors(
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

function TCandidateNoGuiWidgetSet.GetDpiForMonitor(
  Monitor: HMONITOR;
  DpiType: TMonitorDpiType;
  out DpiX: UINT;
  out DpiY: UINT): HRESULT;
begin
  DpiX := 96;
  DpiY := 96;
  Result := S_OK;
end;

function TCandidateNoGuiWidgetSet.GetMonitorInfo(
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

function TCandidateNoGuiWidgetSet.MonitorFromPoint(
  Point: TPoint;
  Flags: DWord): HMONITOR;
begin
  Result := 1;
end;

function TCandidateNoGuiWidgetSet.MonitorFromRect(
  Rect: PRect;
  Flags: DWord): HMONITOR;
begin
  Result := 1;
end;

function TCandidateNoGuiWidgetSet.MonitorFromWindow(
  Window: HWND;
  Flags: DWord): HMONITOR;
begin
  Result := 1;
end;
{$ENDIF}

{$IFDEF V3_NOGUI}
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
  if FindWSRegistered(TCustomEdit) = nil then
    RegisterWSComponent(TCustomEdit, TWSCustomEdit);
  if FindWSRegistered(TButtonControl) = nil then
    RegisterWSComponent(TButtonControl, TWSButtonControl);
  if FindWSRegistered(TCustomCheckBox) = nil then
    RegisterWSComponent(TCustomCheckBox, TWSCustomCheckBox);
  if FindWSRegistered(TCustomFloatSpinEdit) = nil then
    RegisterWSComponent(
      TCustomFloatSpinEdit,
      TWSCustomFloatSpinEdit);
end;
{$ENDIF}

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

function ReadFileBytes(const Path: string): RawByteString;
var
  FileStream: TFileStream;
begin
  if not FileExists(Path) then
    raise Exception.Create('bound file does not exist: ' + Path);
  FileStream := TFileStream.Create(
    Path,
    fmOpenRead or fmShareDenyWrite);
  try
    if FileStream.Size > High(Integer) then
      raise Exception.Create('bound file is too large: ' + Path);
    SetLength(Result, Integer(FileStream.Size));
    if Length(Result) > 0 then
      FileStream.ReadBuffer(Result[1], Length(Result));
  finally
    FileStream.Free;
  end;
end;

function FileSha256(const Path: string): string;
begin
  Result := Sha256Bytes(ReadFileBytes(Path));
end;

function PathHasIdentity(
  const Path: string;
  const Identity: string): Boolean;
var
  NormalizedIdentity: string;
  NormalizedPath: string;
begin
  NormalizedPath := LowerCase(
    StringReplace(
      ExpandFileName(Path),
      '\',
      '/',
      [rfReplaceAll]));
  NormalizedIdentity := LowerCase(
    StringReplace(Identity, '\', '/', [rfReplaceAll]));
  Result :=
    (NormalizedPath = NormalizedIdentity)
    or (
      (Length(NormalizedPath) > Length(NormalizedIdentity))
      and (
        Copy(
          NormalizedPath,
          Length(NormalizedPath) - Length(NormalizedIdentity),
          Length(NormalizedIdentity) + 1)
        = '/' + NormalizedIdentity));
end;

procedure RequireBoundFile(
  const Path: string;
  const Identity: string;
  const ExpectedSha256: string;
  const LabelText: string);
begin
  if not PathHasIdentity(Path, Identity) then
    raise Exception.Create(
      LabelText + ' file identity mismatch');
  if FileSha256(Path) <> ExpectedSha256 then
    raise Exception.Create(
      LabelText + ' file SHA-256 mismatch');
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
  if (Data = nil) or (Data.JSONType <> jtNumber) then
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
  if (Data = nil)
    or (Data.JSONType <> jtString)
    or (Trim(Data.AsString) = '') then
    raise Exception.Create(Name + ' is not a nonempty string');
  Result := Data.AsString;
end;

procedure ValidateAdapterDescriptor(
  const DescriptorPath: string;
  const Source: string;
  const SourceSha256: string;
  const BuildRecipe: string;
  const BuildRecipeSha256: string;
  const CaseDefinitionSha256: string);
var
  Data: TJSONData;
  Descriptor: TJSONObject;
  FileStream: TFileStream;
begin
  if not PathHasIdentity(
      DescriptorPath,
      ExpectedAdapterDescriptor) then
    raise Exception.Create(
      'adapter descriptor file identity mismatch');
  if not FileExists(DescriptorPath) then
    raise Exception.Create(
      'adapter descriptor file does not exist');
  FileStream := TFileStream.Create(
    DescriptorPath,
    fmOpenRead or fmShareDenyWrite);
  try
    Data := GetJSON(FileStream, True);
  finally
    FileStream.Free;
  end;
  if not (Data is TJSONObject) then
  begin
    Data.Free;
    raise Exception.Create(
      'adapter descriptor root is not an object');
  end;
  Descriptor := TJSONObject(Data);
  try
    RequireExactObjectFields(
      Descriptor,
      [
        'adapterId',
        'buildRecipe',
        'buildRecipeSha256',
        'caseDefinition',
        'caseDefinitionSha256',
        'legacyBundle',
        'legacyBundleSha256',
        'legacyReferenceDefinition',
        'legacyReferenceDefinitionSha256',
        'legacyRevision',
        'legacyTree',
        'source',
        'sourceSha256',
        'versionId'
      ]);
    if RequireString(Descriptor, 'adapterId')
        <> ExpectedAdapterId then
      raise Exception.Create(
        'adapter descriptor adapter ID mismatch');
    if RequireString(Descriptor, 'versionId')
        <> ExpectedVersionId then
      raise Exception.Create(
        'adapter descriptor version ID mismatch');
    if RequireString(Descriptor, 'source') <> Source then
      raise Exception.Create(
        'adapter descriptor source identity mismatch');
    if RequireString(Descriptor, 'sourceSha256')
        <> SourceSha256 then
      raise Exception.Create(
        'adapter descriptor source SHA-256 mismatch');
    if RequireString(Descriptor, 'buildRecipe')
        <> BuildRecipe then
      raise Exception.Create(
        'adapter descriptor build recipe identity mismatch');
    if RequireString(Descriptor, 'buildRecipeSha256')
        <> BuildRecipeSha256 then
      raise Exception.Create(
        'adapter descriptor build recipe SHA-256 mismatch');
    if RequireString(Descriptor, 'caseDefinition')
        <> ExpectedCaseDefinition then
      raise Exception.Create(
        'adapter descriptor case definition identity mismatch');
    if RequireString(Descriptor, 'caseDefinitionSha256')
        <> CaseDefinitionSha256 then
      raise Exception.Create(
        'adapter descriptor case definition SHA-256 mismatch');
    if RequireString(Descriptor, 'legacyReferenceDefinition')
        <> ExpectedLegacyReferenceDefinition then
      raise Exception.Create(
        'adapter descriptor legacy reference identity mismatch');
    if RequireString(
        Descriptor,
        'legacyReferenceDefinitionSha256')
        <> ExpectedLegacyReferenceDefinitionSha256 then
      raise Exception.Create(
        'adapter descriptor legacy reference SHA-256 mismatch');
    if RequireString(Descriptor, 'legacyBundle')
        <> ExpectedLegacyBundle then
      raise Exception.Create(
        'adapter descriptor legacy bundle identity mismatch');
    if RequireString(Descriptor, 'legacyBundleSha256')
        <> ExpectedLegacyBundleSha256 then
      raise Exception.Create(
        'adapter descriptor legacy bundle SHA-256 mismatch');
    if RequireString(Descriptor, 'legacyRevision')
        <> ExpectedLegacyRevision then
      raise Exception.Create(
        'adapter descriptor legacy revision mismatch');
    if RequireString(Descriptor, 'legacyTree')
        <> ExpectedLegacyTree then
      raise Exception.Create(
        'adapter descriptor legacy tree mismatch');
  finally
    Descriptor.Free;
  end;
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
  if ArrayData.Count = 0 then
    raise Exception.Create(Name + ' is empty');
  SetLength(Result, ArrayData.Count);
  for Index := 0 to ArrayData.Count - 1 do
  begin
    Data := ArrayData.Items[Index];
    if Data.JSONType <> jtNumber then
      raise Exception.Create(Name + ' contains a non-integer');
    Value := Data.AsInt64;
    if (Value < Minimum) or (Value > Maximum) then
      raise Exception.Create(Name + ' contains an out-of-range value');
    Result[Index] := Integer(Value);
  end;
end;

function RequireReceiverControls(
  const Input: TJSONObject;
  const TotalBlocks: Integer): TReceiverControlArray;
var
  ArrayData: TJSONArray;
  Control: TJSONObject;
  Data: TJSONData;
  Index: Integer;
begin
  Data := Input.Find('controls');
  if (Data = nil) or (Data.JSONType <> jtArray) then
    raise Exception.Create('controls is not an array');
  ArrayData := TJSONArray(Data);
  if ArrayData.Count = 0 then
    raise Exception.Create('controls is empty');
  SetLength(Result, ArrayData.Count);
  for Index := 0 to ArrayData.Count - 1 do
  begin
    Data := ArrayData.Items[Index];
    if Data.JSONType <> jtObject then
      raise Exception.Create('controls contains a non-object');
    Control := TJSONObject(Data);
    RequireExactObjectFields(
      Control,
      ['bandwidthHz', 'block', 'ritHz']);
    Result[Index].Block :=
      RequireInteger(Control, 'block', 0, TotalBlocks - 1);
    Result[Index].RitHz :=
      RequireInteger(Control, 'ritHz', -500, 500);
    Result[Index].BandwidthHz :=
      RequireInteger(Control, 'bandwidthHz', 100, 600);
    if (Index = 0) and (Result[Index].Block <> 0) then
      raise Exception.Create('controls must begin at block zero');
    if (Index > 0)
      and (Result[Index].Block <= Result[Index - 1].Block) then
      raise Exception.Create(
        'control blocks are not strictly increasing');
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
    '","caseDefinitionSha256":"',
    JsonEscape(CaseDefinitionSha256),
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
  raise Exception.Create('fail-if-called abstract stub: ' + Name);
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

constructor TFixedStation.CreateFixed(
  ACollection: TCollection;
  const APitchHz: Integer;
  const AAmplitude: Single;
  const ABlockCount: Integer);
var
  Index: Integer;
begin
  inherited CreateStation;
  Collection := ACollection;
  Pitch := APitchHz;
  Amplitude := AAmplitude;
  WpmS := Ini.Wpm;
  WpmC := WpmS;
  State := stSending;
  TimeOut := NEVER;
  SendPos := 0;
  SetLength(Envelope, ABlockCount * Ini.BufSize);
  for Index := 0 to High(Envelope) do
    Envelope[Index] := AAmplitude;
end;

procedure TFixedStation.ProcessEvent(AEvent: TStationEvent);
begin
  Inc(ProcessEventStubCalls);
  raise Exception.Create(
    'fail-if-called fixed-station ProcessEvent: ' + ToStr(AEvent));
end;

procedure ConfigureIni(const UseSst: Boolean);
begin
  Ini.Call := 'VE3NEA';
  Ini.Wpm := 25;
  Ini.Bandwidth := 500;
  Ini.Pitch := 600;
  Ini.Qsk := False;
  Ini.Rit := 0;
  Ini.RitStepIncr := 50;
  Ini.BufSize := 512;
  Ini.Activity := 2;
  Ini.Qrn := False;
  Ini.Qrm := False;
  Ini.Qsb := False;
  Ini.Flutter := False;
  Ini.Duration := 30;
  Ini.RunMode := rmPileup;
  Ini.SaveWav := False;
  Ini.FarnsworthCharRate := 25;
  Ini.AllStationsWpmS := 0;
  Ini.CallsFromKeyer := False;
  if UseSst then
  begin
    Ini.SimContest := scSst;
    Ini.ActiveContest := @Ini.ContestDefinitions[scSst];
  end
  else
  begin
    Ini.SimContest := scWpx;
    Ini.ActiveContest := @Ini.ContestDefinitions[scWpx];
  end;
end;

procedure CreateHandlelessMainForm;
var
  Index: Integer;
begin
  MainForm := TMainForm.CreateNew(nil);
  MainForm.Panel2 := TPanel.Create(MainForm);
  MainForm.Panel4 := TPanel.Create(MainForm);
  MainForm.Panel7 := TPanel.Create(MainForm);
  MainForm.Panel8 := TPanel.Create(MainForm);
  MainForm.Panel8.Width := 225;
  MainForm.Shape2 := TShape.Create(MainForm);
  MainForm.Shape2.Width := Ini.Bandwidth div 9;
  MainForm.Shape2.Left :=
    (MainForm.Panel8.Width - MainForm.Shape2.Width) div 2;
  MainForm.ComboBox2 := TComboBox.Create(MainForm);
  for Index := 0 to 10 do
    MainForm.ComboBox2.Items.Add(IntToStr(100 + Index * 50));
  MainForm.CheckBox1 := TCheckBox.Create(MainForm);
  MainForm.SpinEdit1 := TSpinEdit.Create(MainForm);
  MainForm.SpinEdit1.MinValue := 10;
  MainForm.SpinEdit1.MaxValue := 120;
  MainForm.SpinEdit1.Value := 25;
  MainForm.VolumeSlider1 := TVolumeSlider.Create(MainForm);
  MainForm.VolumeSlider1.DbMax := 0;
  MainForm.VolumeSlider1.DbScale := 60;
  MainForm.VolumeSlider1.HintStep := 3;
  MainForm.VolumeSlider1.Db := 0;
  MainForm.AlWavFile1 := TAlWavFile.Create(MainForm);
  MainForm.AlSoundOut1 := TAlSoundOut.Create(MainForm);
  MainForm.AlSoundOut1.Enabled := False;
end;

procedure RequireNoHandles;
begin
  if MainForm.HandleAllocated
    or MainForm.Panel2.HandleAllocated
    or MainForm.Panel4.HandleAllocated
    or MainForm.Panel7.HandleAllocated
    or MainForm.Panel8.HandleAllocated
    or MainForm.ComboBox2.HandleAllocated
    or MainForm.CheckBox1.HandleAllocated
    or MainForm.SpinEdit1.HandleAllocated then
    raise Exception.Create('a control handle was allocated');
end;

procedure CreateRuntime(
  const UseSst: Boolean;
  const Seed: Integer);
begin
  AbstractStubCalls := 0;
  ProcessEventStubCalls := 0;
  ConfigureIni(UseSst);
  RandSeed := Seed;
  gDXCCList := TDXCC.Create;
  if UseSst then
    Keyer := TFarnsKeyer.Create(DEFAULTRATE, Ini.BufSize)
  else
    MakeKeyer(DEFAULTRATE, Ini.BufSize);
  if UseSst then
    Tst := TCWSST.Create
  else
    Tst := TFailClosedContest.Create;
  CreateHandlelessMainForm;
  MainForm.SetBw(8);
  MainForm.SetQsk(False);
  MainForm.SetWpm(Ini.Wpm);
  RequireNoHandles;
  if MainForm.AlSoundOut1.Enabled then
    raise Exception.Create('sound output was enabled');
  if MainForm.AlWavFile1.IsOpen then
    raise Exception.Create('WAV output was opened');
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
end;

procedure RequireRuntimeSafety(
  const LabelText: string;
  const Values: TStrings);
begin
  RequireNoHandles;
  if AbstractStubCalls <> 0 then
    raise Exception.Create('an abstract contest stub was called');
  if ProcessEventStubCalls <> 0 then
    raise Exception.Create(
      'the fixed station ProcessEvent stub was called');
  if MainForm.AlSoundOut1.Enabled then
    raise Exception.Create('sound output became enabled');
  if MainForm.AlWavFile1.IsOpen then
    raise Exception.Create('WAV output became open');
  Values.Add(
    LabelText
    + '|handles=0'
    + '|abstract-stub-calls=0'
    + '|process-event-stub-calls=0'
    + '|sound-enabled=false'
    + '|wav-open=false');
end;

procedure RequireRuntimeTeardown(
  const LabelText: string;
  const Values: TStrings);
begin
  if Assigned(Tst)
    or Assigned(Keyer)
    or Assigned(gDXCCList)
    or Assigned(MainForm) then
    raise Exception.Create('runtime teardown left a global assigned');
  Values.Add(
    LabelText
    + '|tst=nil|keyer=nil|gdxcc-list=nil|main-form=nil');
end;

function ObserveAudioBlock(
  const RequestNumber: Integer;
  const Data: TSingleArray): TAudioBlockObservation;
var
  Index: Integer;
  Sample: Double;
begin
  Result.RequestNumber := RequestNumber;
  Result.AbsoluteBlock := Tst.BlockNumber;
  Result.SampleCount := Length(Data);
  Result.Peak := 0;
  Result.Rms := 0;
  for Index := 0 to High(Data) do
  begin
    Sample := Data[Index] / 32768;
    Result.Peak := Max(Result.Peak, Abs(Sample));
    Result.Rms := Result.Rms + Sqr(Sample);
  end;
  if Length(Data) > 0 then
    Result.Rms := Sqrt(Result.Rms / Length(Data));
  Result.FloatSha256 := FloatBlockSha256(Data);
end;

function CaptureReceiverRun(
  const Controls: TReceiverControlArray;
  const TotalBlocks: Integer;
  const RemoteAmplitude: Single;
  const RemoteBfoHz: Integer;
  const ApplyControls: Boolean;
  const LabelText: string;
  const Values: TStrings): TAudioBlockObservations;
const
  StartupRequestCount = 5;
  Seed = 12345;
var
  BlockData: TSingleArray;
  ControlIndex: Integer;
  FullBlock: Integer;
  RequestIndex: Integer;

  procedure ClickRitLeft;
  begin
    MainForm.Panel8MouseDown(
      MainForm.Panel8,
      mbLeft,
      [],
      MainForm.Shape2.Left - 1,
      0);
  end;

  procedure ClickRitRight;
  begin
    MainForm.Panel8MouseDown(
      MainForm.Panel8,
      mbLeft,
      [],
      MainForm.Shape2.Left + MainForm.Shape2.Width + 1,
      0);
  end;

  procedure SetRitByHandlers(const TargetHz: Integer);
  begin
    if (TargetHz mod Abs(Ini.RitStepIncr)) <> 0 then
      raise Exception.Create(
        'RIT target is unreachable through the configured handler step');
    while Ini.Rit < TargetHz do
      ClickRitRight;
    while Ini.Rit > TargetHz do
      ClickRitLeft;
    if Ini.Rit <> TargetHz then
      raise Exception.Create('RIT handler target was not reached');
  end;

  procedure ApplyControl(const Control: TReceiverControl);
  var
    BandwidthIndex: Integer;
  begin
    BandwidthIndex := (Control.BandwidthHz - 100) div 50;
    if (100 + BandwidthIndex * 50) <> Control.BandwidthHz then
      raise Exception.Create(
        'bandwidth is unreachable through TMainForm.SetBw');
    MainForm.SetBw(BandwidthIndex);
    SetRitByHandlers(Control.RitHz);
  end;

begin
  SetLength(Result, StartupRequestCount + TotalBlocks);
  CreateRuntime(False, Seed);
  try
    TFixedStation.CreateFixed(
      Tst.Stations,
      RemoteBfoHz,
      RemoteAmplitude,
      TotalBlocks + 8);
    for RequestIndex := 0 to StartupRequestCount - 1 do
    begin
      BlockData := Tst.GetAudio;
      Result[RequestIndex] := ObserveAudioBlock(
        RequestIndex + 1,
        BlockData);
    end;

    ControlIndex := 0;
    for FullBlock := 0 to TotalBlocks - 1 do
    begin
      if ApplyControls
        and (ControlIndex <= High(Controls))
        and (Controls[ControlIndex].Block = FullBlock) then
      begin
        ApplyControl(Controls[ControlIndex]);
        Inc(ControlIndex);
      end;
      BlockData := Tst.GetAudio;
      Result[StartupRequestCount + FullBlock] :=
        ObserveAudioBlock(
          StartupRequestCount + FullBlock + 1,
          BlockData);
    end;
    if ApplyControls and (ControlIndex <> Length(Controls)) then
      raise Exception.Create('not every receiver control was applied');
    RequireRuntimeSafety(LabelText + '-safety', Values);
  finally
    DestroyRuntime;
  end;
  RequireRuntimeTeardown(LabelText + '-teardown', Values);
end;

procedure AddReceiverComparison(
  const Values: TStrings;
  const Baseline: TAudioBlockObservations;
  const Controlled: TAudioBlockObservations;
  const TotalBlocks: Integer);
const
  StartupRequestCount = 5;
var
  FullBlock: Integer;
  Index: Integer;
begin
  if Length(Baseline) <> Length(Controlled) then
    raise Exception.Create('receiver runs have different request counts');
  Values.Add(
    'startup-request-count=5|sample-counts=1,1,1,1,1'
    + '|first-full-absolute-block=6');
  for Index := 0 to StartupRequestCount - 1 do
  begin
    if (Baseline[Index].SampleCount <> 1)
      or (Controlled[Index].SampleCount <> 1)
      or (Baseline[Index].AbsoluteBlock <> Index + 1)
      or (Controlled[Index].AbsoluteBlock <> Index + 1) then
      raise Exception.Create('legacy startup request shape changed');
    Values.Add(
      'startup[' + IntToStr(Index) + ']'
      + '|absolute-block='
      + IntToStr(Controlled[Index].AbsoluteBlock)
      + '|samples=1'
      + '|baseline-sha256=' + Baseline[Index].FloatSha256
      + '|controlled-sha256=' + Controlled[Index].FloatSha256
      + '|equal='
      + LowerCase(BoolToStr(
        Baseline[Index].FloatSha256 =
          Controlled[Index].FloatSha256,
        True)));
  end;

  for FullBlock := 0 to TotalBlocks - 1 do
  begin
    Index := StartupRequestCount + FullBlock;
    if (Baseline[Index].SampleCount <> Ini.DEFAULTBUFSIZE)
      or (Controlled[Index].SampleCount <> Ini.DEFAULTBUFSIZE) then
      raise Exception.Create('legacy full audio block size changed');
    Values.Add(
      'block[' + IntToStr(FullBlock) + ']'
      + '|absolute-block='
      + IntToStr(Controlled[Index].AbsoluteBlock)
      + '|filter-swap='
      + LowerCase(BoolToStr(
        (Controlled[Index].AbsoluteBlock mod 10) = 0,
        True))
      + '|baseline-sha256=' + Baseline[Index].FloatSha256
      + '|controlled-sha256=' + Controlled[Index].FloatSha256
      + '|equal='
      + LowerCase(BoolToStr(
        Baseline[Index].FloatSha256 =
          Controlled[Index].FloatSha256,
        True))
      + '|controlled-peak='
      + FloatValue(Controlled[Index].Peak)
      + '|controlled-rms='
      + FloatValue(Controlled[Index].Rms));
  end;
end;

function CaptureRitSequence(
  const StepHz: Integer;
  const HstMode: Boolean;
  const Direction: Integer;
  const Count: Integer;
  const ResetAfter: Boolean;
  const LabelText: string;
  const Values: TStrings): string;
var
  Index: Integer;
  X: Integer;
begin
  CreateRuntime(False, 12345);
  try
    Ini.RitStepIncr := StepHz;
    if HstMode then
      Ini.RunMode := rmHst
    else
      Ini.RunMode := rmPileup;
    Result := IntToStr(Ini.Rit);
    for Index := 1 to Count do
    begin
      if Direction < 0 then
        X := MainForm.Shape2.Left - 1
      else
        X :=
          MainForm.Shape2.Left + MainForm.Shape2.Width + 1;
      MainForm.Panel8MouseDown(
        MainForm.Panel8,
        mbLeft,
        [],
        X,
        0);
      Result := Result + ',' + IntToStr(Ini.Rit);
    end;
    if ResetAfter then
    begin
      MainForm.Shape2MouseDown(
        MainForm.Shape2,
        mbLeft,
        [],
        0,
        0);
      Result := Result + ',' + IntToStr(Ini.Rit);
    end;
    RequireRuntimeSafety(LabelText + '-safety', Values);
  finally
    DestroyRuntime;
  end;
  RequireRuntimeTeardown(LabelText + '-teardown', Values);
end;

procedure ObserveRit(
  const Values: TStrings;
  const Input: TJSONObject);
var
  Baseline: TAudioBlockObservations;
  BoundaryCommandCount: Integer;
  Controlled: TAudioBlockObservations;
  Controls: TReceiverControlArray;
  RemoteAmplitude: Integer;
  RemoteBfoHz: Integer;
  ReverseStepHz: Integer;
  RitStepHz: Integer;
  TotalBlocks: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'blockSize',
      'boundaryCommandCount',
      'controls',
      'pitchHz',
      'remoteAmplitude',
      'remoteBfoHz',
      'reverseRitStepHz',
      'ritStepHz',
      'sampleRate',
      'scenario',
      'totalBlocks'
    ]);
  if RequireInteger(Input, 'sampleRate', 1, MaxInt) <> DEFAULTRATE then
    raise Exception.Create('sample rate is not the CE default');
  if RequireInteger(Input, 'blockSize', 1, MaxInt) <> DEFAULTBUFSIZE then
    raise Exception.Create('block size is not the CE default');
  RequireInteger(Input, 'pitchHz', 600, 600);
  TotalBlocks := RequireInteger(Input, 'totalBlocks', 1, 64);
  BoundaryCommandCount :=
    RequireInteger(Input, 'boundaryCommandCount', 1, 32);
  RitStepHz := RequireInteger(Input, 'ritStepHz', 1, 500);
  ReverseStepHz :=
    RequireInteger(Input, 'reverseRitStepHz', -500, -1);
  RemoteAmplitude :=
    RequireInteger(Input, 'remoteAmplitude', 18000, 18000);
  RemoteBfoHz := RequireInteger(Input, 'remoteBfoHz', 360, 360);
  Controls := RequireReceiverControls(Input, TotalBlocks);

  Values.Add(
    'contract=ce-runtime-rit-v3'
    + '|sample-rate=' + IntToStr(DEFAULTRATE)
    + '|block-size=' + IntToStr(DEFAULTBUFSIZE)
    + '|pitch-hz=600|initial-bandwidth-hz=500'
    + '|settings=explicit-case-stimulus'
    + '|clean-no-ini-ui-defaults=450/550'
    + '|main-handler=Panel8MouseDown/Shape2MouseDown'
    + '|fine-directions-excluded=true');
  Values.Add(
    'rit-normal-up='
    + CaptureRitSequence(
      RitStepHz,
      False,
      1,
      BoundaryCommandCount,
      False,
      'rit-normal-up',
      Values));
  Values.Add(
    'rit-normal-down='
    + CaptureRitSequence(
      RitStepHz,
      False,
      -1,
      BoundaryCommandCount,
      False,
      'rit-normal-down',
      Values));
  Values.Add(
    'rit-reversed-right='
    + CaptureRitSequence(
      ReverseStepHz,
      False,
      1,
      4,
      False,
      'rit-reversed',
      Values));
  Values.Add(
    'rit-hst-right='
    + CaptureRitSequence(
      ReverseStepHz,
      True,
      1,
      4,
      False,
      'rit-hst',
      Values));
  Values.Add(
    'rit-reset='
    + CaptureRitSequence(
      RitStepHz,
      False,
      1,
      1,
      True,
      'rit-reset',
      Values));

  Values.Add(
    'receiver-runs=2|graph-recreated=true|rand-seed=12345'
    + '|fixed-station-class=TFixedStation'
    + '|remote-stimulus=synthetic'
    + '|remote-bfo-hz=' + IntToStr(RemoteBfoHz)
    + '|remote-amplitude=' + IntToStr(RemoteAmplitude)
    + '|station-get-block=legacy');
  Baseline := CaptureReceiverRun(
    Controls,
    TotalBlocks,
    RemoteAmplitude,
    RemoteBfoHz,
    False,
    'rit-baseline',
    Values);
  Controlled := CaptureReceiverRun(
    Controls,
    TotalBlocks,
    RemoteAmplitude,
    RemoteBfoHz,
    True,
    'rit-controlled',
    Values);
  AddReceiverComparison(Values, Baseline, Controlled, TotalBlocks);
end;

procedure ObserveBandwidth(
  const Values: TStrings;
  const Input: TJSONObject);
var
  Baseline: TAudioBlockObservations;
  Controlled: TAudioBlockObservations;
  Controls: TReceiverControlArray;
  RemoteAmplitude: Integer;
  RemoteBfoHz: Integer;
  TotalBlocks: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'blockSize',
      'controls',
      'pitchHz',
      'remoteAmplitude',
      'remoteBfoHz',
      'sampleRate',
      'scenario',
      'totalBlocks'
    ]);
  if RequireInteger(Input, 'sampleRate', 1, MaxInt) <> DEFAULTRATE then
    raise Exception.Create('sample rate is not the CE default');
  if RequireInteger(Input, 'blockSize', 1, MaxInt) <> DEFAULTBUFSIZE then
    raise Exception.Create('block size is not the CE default');
  RequireInteger(Input, 'pitchHz', 600, 600);
  TotalBlocks := RequireInteger(Input, 'totalBlocks', 1, 64);
  RemoteAmplitude :=
    RequireInteger(Input, 'remoteAmplitude', 18000, 18000);
  RemoteBfoHz := RequireInteger(Input, 'remoteBfoHz', 360, 360);
  Controls := RequireReceiverControls(Input, TotalBlocks);

  Values.Add(
    'contract=ce-runtime-bandwidth-v3'
    + '|sample-rate=' + IntToStr(DEFAULTRATE)
    + '|block-size=' + IntToStr(DEFAULTBUFSIZE)
    + '|pitch-hz=600|initial-bandwidth-hz=500'
    + '|settings=explicit-case-stimulus'
    + '|clean-no-ini-ui-defaults=450/550'
    + '|main-method=TMainForm.SetBw'
    + '|audio-method=TContest.GetAudio');
  Values.Add(
    'receiver-runs=2|graph-recreated=true|rand-seed=12345'
    + '|fixed-station-class=TFixedStation'
    + '|remote-stimulus=synthetic'
    + '|remote-bfo-hz=' + IntToStr(RemoteBfoHz)
    + '|remote-amplitude=' + IntToStr(RemoteAmplitude)
    + '|station-get-block=legacy');
  Baseline := CaptureReceiverRun(
    Controls,
    TotalBlocks,
    RemoteAmplitude,
    RemoteBfoHz,
    False,
    'bandwidth-baseline',
    Values);
  Controlled := CaptureReceiverRun(
    Controls,
    TotalBlocks,
    RemoteAmplitude,
    RemoteBfoHz,
    True,
    'bandwidth-controlled',
    Values);
  AddReceiverComparison(Values, Baseline, Controlled, TotalBlocks);
end;

function EnvelopeTransitions(
  const Envelope: TSingleArray;
  const TrueLength: Integer;
  const Threshold: Single): string;
var
  Index: Integer;
  Marked: Boolean;
  NextMarked: Boolean;
begin
  if (TrueLength <= 0) or (TrueLength > Length(Envelope)) then
    raise Exception.Create('envelope true length is invalid');
  Marked := Envelope[0] >= Threshold;
  Result := '0:' + IntToStr(Ord(Marked));
  for Index := 1 to TrueLength - 1 do
  begin
    NextMarked := Envelope[Index] >= Threshold;
    if NextMarked <> Marked then
    begin
      Marked := NextMarked;
      Result :=
        Result + ',' + IntToStr(Index)
        + ':' + IntToStr(Ord(Marked));
    end;
  end;
  Result := Result + ',' + IntToStr(TrueLength) + ':end';
end;

procedure ObserveSst(
  const Values: TStrings;
  const Input: TJSONObject);
var
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
      'blockSize',
      'characterWpm',
      'messages',
      'sampleRate',
      'scenario',
      'sendingWpm'
    ]);
  SampleRate := RequireInteger(Input, 'sampleRate', 1, MaxInt);
  BlockSize := RequireInteger(Input, 'blockSize', 1, MaxInt);
  if (SampleRate <> DEFAULTRATE)
    or (BlockSize <> DEFAULTBUFSIZE) then
    raise Exception.Create('SST vector is not using CE audio defaults');
  SendingWpm := RequireInteger(Input, 'sendingWpm', 10, 120);
  CharacterWpm := RequireInteger(Input, 'characterWpm', 10, 120);
  Messages := RequireStringArray(Input, 'messages');

  Values.Add(
    'contract=ce-runtime-sst-farnsworth-v3'
    + '|contest-class=TCWSST'
    + '|station-class=TMyStation'
    + '|send-path=Tst.SendText'
    + '|keyer-class=TFarnsKeyer'
    + '|graph-recreated-per-message=true'
    + '|rand-seed=12345');
  for Index := 0 to High(Messages) do
  begin
    CreateRuntime(True, 12345);
    try
      Ini.FarnsworthCharRate := CharacterWpm;
      MainForm.SetWpm(SendingWpm);
      Tst.SendText(Tst.Me, Messages[Index]);
      if not (Tst is TCWSST)
        or not (Keyer is TFarnsKeyer)
        or not Tst.IsFarnsworthAllowed then
        raise Exception.Create('SST runtime selection path was not active');
      Values.Add(
        'message[' + IntToStr(Index) + ']=' + Messages[Index]);
      Values.Add(
        'runtime[' + IntToStr(Index) + ']'
        + '|contest=' + Tst.ClassName
        + '|station=' + Tst.Me.ClassName
        + '|keyer=' + Keyer.ClassName
        + '|sending-wpm=' + IntToStr(Tst.Me.WpmS)
        + '|character-wpm=' + IntToStr(Tst.Me.WpmC));
      Values.Add(
        'encoded[' + IntToStr(Index) + ']=' + Keyer.MorseMsg);
      Values.Add(
        'true-length[' + IntToStr(Index) + ']='
        + IntToStr(Keyer.TrueEnvelopeLen));
      Values.Add(
        'padded-length[' + IntToStr(Index) + ']='
        + IntToStr(Length(Tst.Me.Envelope)));
      Values.Add(
        'transitions[' + IntToStr(Index) + ']='
        + EnvelopeTransitions(
          Tst.Me.Envelope,
          Keyer.TrueEnvelopeLen,
          Tst.Me.Amplitude * 0.5));
      Values.Add(
        'float-sha256[' + IntToStr(Index) + ']='
        + FloatBlockSha256(Tst.Me.Envelope));
      RequireRuntimeSafety(
        'sst-message-' + IntToStr(Index) + '-safety',
        Values);
    finally
      DestroyRuntime;
    end;
    RequireRuntimeTeardown(
      'sst-message-' + IntToStr(Index) + '-teardown',
      Values);
  end;
end;

procedure ObserveLocalAudio(
  const Values: TStrings;
  const Input: TJSONObject;
  const Qsk: Boolean);
const
  StartupRequestCount = 5;
var
  BlockData: TSingleArray;
  FullBlock: Integer;
  LevelDb: Integer;
  LevelIndex: Integer;
  LocalText: string;
  LocalWpm: Integer;
  MonitorLevels: TIntegerArray;
  RemoteAmplitude: Integer;
  RemoteBfoHz: Integer;
  StartupHash: string;
  StartupRequestTotal: Integer;
  TotalBlocks: Integer;
begin
  RequireExactObjectFields(
    Input,
    [
      'bandwidthHz',
      'blockSize',
      'localAmplitude',
      'localText',
      'localWpm',
      'monitorLevelsDb',
      'pitchHz',
      'remoteAmplitude',
      'remoteBfoHz',
      'sampleRate',
      'scenario',
      'totalBlocks'
    ]);
  if RequireInteger(Input, 'sampleRate', 1, MaxInt) <> DEFAULTRATE then
    raise Exception.Create('sample rate is not the CE default');
  if RequireInteger(Input, 'blockSize', 1, MaxInt) <> DEFAULTBUFSIZE then
    raise Exception.Create('block size is not the CE default');
  RequireInteger(Input, 'bandwidthHz', 500, 500);
  RequireInteger(Input, 'pitchHz', 600, 600);
  RequireInteger(Input, 'localAmplitude', 300000, 300000);
  LocalText := RequireString(Input, 'localText');
  LocalWpm := RequireInteger(Input, 'localWpm', 10, 120);
  MonitorLevels :=
    RequireIntegerArray(Input, 'monitorLevelsDb', -60, 0);
  RemoteAmplitude :=
    RequireInteger(Input, 'remoteAmplitude', 18000, 18000);
  RemoteBfoHz := RequireInteger(Input, 'remoteBfoHz', 180, 180);
  TotalBlocks := RequireInteger(Input, 'totalBlocks', 1, 32);

  Values.Add(
    'contract=ce-runtime-local-audio-v3'
    + '|qsk=' + LowerCase(BoolToStr(Qsk, True))
    + '|pitch-hz=600|bandwidth-hz=500'
    + '|settings=explicit-case-stimulus'
    + '|clean-no-ini-ui-defaults=450/550'
    + '|main-method=TMainForm.SetQsk'
    + '|send-path=Tst.SendText/TMyStation'
    + '|audio-method=TContest.GetAudio'
    + '|graph-recreated-per-level=true'
    + '|remote-stimulus=synthetic'
    + '|remote-bfo-hz=' + IntToStr(RemoteBfoHz)
    + '|remote-amplitude=' + IntToStr(RemoteAmplitude)
    + '|rand-seed=12345');
  StartupRequestTotal := 0;
  for LevelIndex := 0 to High(MonitorLevels) do
  begin
    LevelDb := MonitorLevels[LevelIndex];
    BlockData := nil;
    CreateRuntime(False, 12345);
    try
      MainForm.SetWpm(LocalWpm);
      MainForm.SetQsk(Qsk);
      MainForm.VolumeSlider1.Db := LevelDb;
      Tst.SendText(Tst.Me, LocalText);
      if Tst.Me.State <> stSending then
        raise Exception.Create('local station did not enter sending state');
      Values.Add(
        'level[' + IntToStr(LevelIndex) + ']'
        + '|db=' + IntToStr(LevelDb)
        + '|slider-value='
        + FloatValue(MainForm.VolumeSlider1.Value)
        + '|qsk-ini='
        + LowerCase(BoolToStr(Ini.Qsk, True))
        + '|qsk-control='
        + LowerCase(BoolToStr(MainForm.CheckBox1.Checked, True))
        + '|local-envelope-length='
        + IntToStr(Length(Tst.Me.Envelope))
        + '|keyer=' + Keyer.ClassName);
      TFixedStation.CreateFixed(
        Tst.Stations,
        RemoteBfoHz,
        RemoteAmplitude,
        TotalBlocks + 8);

      for FullBlock := 0 to StartupRequestCount - 1 do
      begin
        BlockData := nil;
        BlockData := Tst.GetAudio;
        if (Length(BlockData) <> 1)
          or (Tst.BlockNumber <> FullBlock + 1) then
          raise Exception.Create('local startup request shape changed');
        StartupHash := FloatBlockSha256(BlockData);
        if StartupHash <> ExpectedZeroSingleSha256 then
          raise Exception.Create(
            'local startup request is not one zero Single sample');
        Inc(StartupRequestTotal);
        Values.Add(
          'level[' + IntToStr(LevelIndex) + ']'
          + '|startup[' + IntToStr(FullBlock) + ']'
          + '|absolute-block=' + IntToStr(Tst.BlockNumber)
          + '|samples=1'
          + '|float-sha256=' + StartupHash);
      end;
      for FullBlock := 0 to TotalBlocks - 1 do
      begin
        BlockData := Tst.GetAudio;
        if Length(BlockData) <> DEFAULTBUFSIZE then
          raise Exception.Create('local full audio block size changed');
        with ObserveAudioBlock(
          StartupRequestCount + FullBlock + 1,
          BlockData) do
          Values.Add(
            'level[' + IntToStr(LevelIndex) + ']'
            + '|block[' + IntToStr(FullBlock) + ']'
            + '|absolute-block=' + IntToStr(AbsoluteBlock)
            + '|filter-swap='
            + LowerCase(BoolToStr(
              (AbsoluteBlock mod 10) = 0,
              True))
            + '|peak=' + FloatValue(Peak)
            + '|rms=' + FloatValue(Rms)
            + '|float-sha256=' + FloatSha256);
      end;
      RequireRuntimeSafety(
        'local-level-' + IntToStr(LevelIndex) + '-safety',
        Values);
    finally
      DestroyRuntime;
    end;
    RequireRuntimeTeardown(
      'local-level-' + IntToStr(LevelIndex) + '-teardown',
      Values);
  end;
  if StartupRequestTotal
      <> Length(MonitorLevels) * StartupRequestCount then
    raise Exception.Create(
      'local startup zero-sample assertion count changed');
  Values.Add(
    'startup-all-levels-zero-single-sha256='
    + ExpectedZeroSingleSha256
    + '|request-count=' + IntToStr(StartupRequestTotal));
end;

var
  AdapterId: string;
  BuildRecipe: string;
  BuildRecipePath: string;
  BuildRecipeSha256: string;
  CaseDefinitionPath: string;
  CaseDefinitionSha256: string;
  DescriptorPath: string;
  ExitStatus: Integer;
  Input: TJSONObject;
  InputPath: string;
  InputSha256: string;
  Scenario: string;
  Source: string;
  SourcePath: string;
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
    if ParamCount <> 15 then
      raise Exception.Create(
        'usage: LegacyOracle <legacy-root-with-separator> <scenario> '
        + '<adapter-id> <version-id> <source> <source-sha256> '
        + '<build-recipe> <build-recipe-sha256> '
        + '<case-definition-sha256> <input-sha256> '
        + '<content-addressed-input-json> <source-file> '
        + '<build-recipe-file> <case-definition-file> '
        + '<adapter-descriptor-file>');
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
    SourcePath := ParamStr(12);
    BuildRecipePath := ParamStr(13);
    CaseDefinitionPath := ParamStr(14);
    DescriptorPath := ParamStr(15);
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

    RequireBoundFile(
      SourcePath,
      ExpectedSource,
      SourceSha256,
      'source');
    RequireBoundFile(
      BuildRecipePath,
      ExpectedBuildRecipe,
      BuildRecipeSha256,
      'build recipe');
    RequireBoundFile(
      CaseDefinitionPath,
      ExpectedCaseDefinition,
      CaseDefinitionSha256,
      'case definition');
    ValidateAdapterDescriptor(
      DescriptorPath,
      Source,
      SourceSha256,
      BuildRecipe,
      BuildRecipeSha256,
      CaseDefinitionSha256);
    if FileSha256(ParamStr(1) + 'DXCC.LIST')
        <> ExpectedDxccListSha256 then
      raise Exception.Create('DXCC.LIST SHA-256 mismatch');

    Input := LoadScenarioInput(InputPath, Scenario, InputSha256);
    Values := TStringList.Create;
{$IFDEF V3_NOGUI}
    CreateWidgetset(TCandidateNoGuiWidgetSet);
    RegisterNoguiHandlelessClasses;
{$ENDIF}
    Application.Initialize;

    if Scenario = 'audio.rit-live-control' then
      ObserveRit(Values, Input)
    else if Scenario = 'audio.bandwidth-live-control' then
      ObserveBandwidth(Values, Input)
    else if Scenario = 'audio.operator-sidetone-pipeline' then
      ObserveLocalAudio(Values, Input, False)
    else if Scenario = 'audio.qsk-receiver-ducking' then
      ObserveLocalAudio(Values, Input, True)
    else if Scenario = 'audio.sst-farnsworth-timing' then
      ObserveSst(Values, Input)
    else
      raise Exception.Create('unsupported scenario: ' + Scenario);

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
{$IFDEF V3_NOGUI}
  FreeWidgetSet;
{$ENDIF}
  if ExitStatus <> 0 then
    Halt(ExitStatus);
end.
