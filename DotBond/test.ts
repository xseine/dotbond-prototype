class GFG {
    private add0(a: number, b: number): number {
        let sum = a + b;
        return sum;
    };

    private add1(a: number, b: number, c: number): number {
        let sum = a + b + c;
        return sum;
    };

    public add(a: number, b: number): number ;
    public add(a: number, b: number, c: number): number ;
    public add(a: number, b: number, c?: number): number {
        return (() => {
            if (true && typeof a == 'number' && typeof b == 'number' && c == null) return this.add0(a, b);
            else if (true && typeof a == 'number' && typeof b == 'number' && typeof c == 'number') return this.add1(a, b, c);
        })();
    }

    public addComposed(a: number, b: number, c: number | null): number {
        return (() => {
            if (true && typeof a == 'number' && typeof b == 'number' && typeof c == 'number') return this.add(a, b, c);
            else if (true && typeof a == 'number' && typeof b == 'number' && c == null) return this.add(a, b)
                ;
        })();
    };

    public static main(args: string[]): void {

        // Creating Object
        let ob = new GFG();

        let sum1 = ob.add(1, 2);
        console.log("sum of the two "
            + "integer value : " + sum1);

        let sum2 = ob.add(1, 2, 3);
        console.log("sum of the three "
            + "integer value : " + sum2);
    };
}

