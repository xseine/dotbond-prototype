function savo() {
    let console = {};
    let __result = [];
    console.log = function () {
        __result = [...__result, ...arguments];
    };

    class WeatherStation {
        constructor() {
            this.recordDates = [];
            this.temperatures = [];
        }

        acceptReading(reading) {
            this.reading = reading;
            this.recordDates.push(new Date());
            this.temperatures.push(reading.temperature);
        }
        ;

        clearAll() {
            this.reading = new Reading();
            this.recordDates.length = 0;
            this.temperatures.length = 0;
        }
        ;

        get latestTemperature() {
            return this.reading.temperature;
        }

        get latestPressure() {
            return this.reading.pressure;
        }

        get latestRainfall() {
            return this.reading.rainfall;
        }

        get hasHistory() {
            return this.recordDates.length > 1;
        }

        get longTermOutlook() {
            return (() => {
                if (this.reading.windDirection == WindDirection.Northerly)
                    return Outlook.Cool;
                else if (this.reading.windDirection == WindDirection.Easterly)
                    return (this.reading.temperature > 20) ? Outlook.Good : Outlook.Warm;
                else if (this.reading.windDirection == WindDirection.Southerly)
                    return Outlook.Good;
                else if (this.reading.windDirection == WindDirection.Westerly)
                    return Outlook.Rainy;
                else
                    throw '';
            })();
        }

        runSelfTest() {
            return this.reading.equals(new Reading()) ? State.Bad : State.Good;
        }
        ;

        get shortTermOutlook() {
            return this.reading.equals(new Reading())
                ?
                (() => {
                    throw '';
                })() :
                (() => {
                    if (true && this.reading.temperature < 30 && this.reading.pressure < 10)
                        return Outlook.Cool;
                    else if (true && this.reading.temperature > 50)
                        return Outlook.Good;
                    else
                        return Outlook.Warm;
                })();
        }
    }

    class Reading {
        constructor(temperature, pressure, rainfall, windDirection) {
            (() => {
                if (true && typeof temperature == 'number' && typeof pressure == 'number' && typeof rainfall == 'number' && windDirection in WindDirection)
                    return this.constructor1(temperature, pressure, rainfall, windDirection);
            })();
        }

        constructor0() {
        }

        constructor1(temperature, pressure, rainfall, windDirection) {
            this.temperature = temperature;
            this.pressure = pressure;
            this.rainfall = rainfall;
            this.windDirection = windDirection;
        }

        /*** Please do not modify this struct ***/ equals(obj) {
            return this.temperature == obj.temperature && this.pressure == obj.pressure && this.rainfall == obj.rainfall && this.windDirection == obj.windDirection;
        }
        ;
    }

    (function (State) {
        State[State["Good"] = 0] = "Good";
        State[State["Bad"] = 1] = "Bad";
    })(State || (State = {}));
    (function (Outlook) {
        Outlook[Outlook["Cool"] = 0] = "Cool";
        Outlook[Outlook["Rainy"] = 1] = "Rainy";
        Outlook[Outlook["Warm"] = 2] = "Warm";
        Outlook[Outlook["Good"] = 3] = "Good";
    })(Outlook || (Outlook = {}));
    (function (WindDirection) {
        WindDirection[WindDirection["Unknown"] = 0] = "Unknown";
        WindDirection[WindDirection["Northerly"] = 1] = "Northerly";
        WindDirection[WindDirection["Easterly"] = 2] = "Easterly";
        WindDirection[WindDirection["Southerly"] = 3] = "Southerly";
        WindDirection[WindDirection["Westerly"] = 4] = "Westerly";
    })(WindDirection || (WindDirection = {}))


    /*** Please do not modify this enum ***/
    var State;
    ;
    /*** Please do not modify this enum ***/
    var Outlook;
    ;
    /*** Please do not modify this enum ***/
    var WindDirection;
    ;
    return __result
}