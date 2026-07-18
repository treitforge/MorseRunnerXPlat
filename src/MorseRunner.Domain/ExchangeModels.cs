namespace MorseRunner.Domain;

public enum ExchangeType1
{
    Rst = 0,
    OperatorName = 1,
    FieldDayClass = 2,
    SweepstakesNumberPrecedence = 3,
    Undefined = 4,
}

public enum ExchangeType2
{
    SerialNumber = 0,
    GenericField = 1,
    ArrlSection = 2,
    StateProvince = 3,
    CqZone = 4,
    ItuZone = 5,
    Age = 6,
    Power = 7,
    JapanPrefecture = 8,
    JapanCity = 9,
    NaqpSecondField = 10,
    NaqpNonNorthAmericaSecondField = 11,
    SweepstakesCheckSection = 12,
    Undefined = 13,
}
