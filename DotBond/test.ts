export class RemoteControlCar {
    private _batteryPercentage: number = 100;
    private _distanceDrivenInMeters: number = 0;
    private _sponsors: string[] = [] as string[];
    private _latestSerialNum: number = 0;

    public drive(): void {
        if (this._batteryPercentage > 0) {
            this._batteryPercentage -= 10;
            this._distanceDrivenInMeters += 2;
        }
    };

    public setSponsors(...sponsors: string[]): void {
        this._sponsors = sponsors;
    };

    public displaySponsor(sponsorNum: number): string {
        return this._sponsors[sponsorNum];
    };

    public getTelemetryData(
    serialNum: number
,
    batteryPercentage: number
,
    distanceDrivenInMeters: number
):
    boolean {
    if(this

.
    _latestSerialNum
>
    serialNum
) {
    serialNum = this._latestSerialNum;
    batteryPercentage = -1;
    distanceDrivenInMeters = -1;
    return
    false;
}

this._latestSerialNum = serialNum;
batteryPercentage = this._batteryPercentage;
distanceDrivenInMeters = this._distanceDrivenInMeters;
return true;
}
;
public static
buy()
:
RemoteControlCar
{
    return new RemoteControlCar();
}
;
}

export class TelemetryClient {
    private _car: RemoteControlCar;

    public constructor(car: RemoteControlCar) {
        this._car = car;
    }

    public getBatteryUsagePerMeter(serialNum: number): string {
        let data = this._car.getTelemetryData(ref
        serialNum, out
        int
        batteryPercentage, out
        int
        distanceDrivenInMeters
    )
        ;
        if (distanceDrivenInMeters > 0 && data)
            return `usage-per-meter=${Math.floor((100 - batteryPercentage) / distanceDrivenInMeters)}`;
        return "no data";
    };
}
