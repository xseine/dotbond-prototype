export class Change {
    public static findFewestCoins(coins: number[], change: number): number[] {
        if (change < 0) throw "Change cannot be negative.";
        if (change > 0 && change < Math.min(...coins)) throw "Change cannot be less than minimal coin value.";

        let a = [] as number[];

        return [...Array(change + 1).keys()].slice(1).reduce(UpdateFewestCoinsForChange, (() => {
            let obj = {} as any;
            obj[0] = [];
            return obj;
        })())[change] ?? (() => {
            throw 'change';
        })();

        function UpdateFewestCoinsForChange(current: { [key: number]: number[] }, subChange: number) {
            let a = current[2];

            let fewestCoins = FewestCoinsForChange(current, subChange);
            if (fewestCoins != null)
                current[subChange] = fewestCoins;
            return current;
        }

        function FewestCoinsForChange(current: { [key: number]: number[] }, subChange: number) {
            return coins.filter(coin => coin <= subChange)
                .map(coin => [coin].concat((current[subChange - coin] ?? [1])))
                .filter(fewestCoins => fewestCoins != null)
                .sort((a, b) => a.length - b.length).find(_ => true);
        };
    };
}

console.log(Change.findFewestCoins([1, 5, 10, 25, 100], 15));
console.log(Change.findFewestCoins([1, 5, 10, 25, 100], 40));
