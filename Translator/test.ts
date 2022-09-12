export class Appointment {
    public static schedule(appointmentDateDescription: string): Date {
        return new Date(appointmentDateDescription);
    };

    public static hasPassed(appointmentDate: Date): boolean {
        return (appointmentDate < new Date());
    };

    public static isAfternoonAppointment(appointmentDate: Date): boolean {
        return (this._afternoonStartHour <= appointmentDate.getHours() && appointmentDate.getHours() < this._afternoonEndHour);
    };

    public static description(appointmentDate: Date): string {
        return `You have an appointment on ${appointmentDate}.`;
    };

    public static anniversaryDate(): Date {
        return new Date('DateTime.Now.Year-_anniversaryMonth-_anniversaryDayT00:00:00.00');
    };

    //------------------------------------------- fixed values
    private static readonly _afternoonStartHour: number = 12;
    private static readonly _afternoonEndHour: number = 18;
    private static readonly _anniversaryMonth: number = 9;
    private static readonly _anniversaryDay: number = 15;
}

