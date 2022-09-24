Date.prototype.addYears = function (num) {
    return new Date(new Date(+this).setFullYear(this.getFullYear() + num));
};
Date.prototype.addMonths = function (num) {
    return new Date(new Date(+this).setMonth(this.getMonth() + num));
};
Date.prototype.addDays = function (num) {
    return new Date(new Date(+this).setDate(this.getDate() + num));
};
Date.prototype.addHours = function (num) {
    return new Date(new Date(+this).setHours(this.getHours() + num));
};
Date.prototype.addMinutes = function (num) {
    return new Date(new Date(+this).setMinutes(this.getMinutes() + num));
};
Date.prototype.addSeconds = function (num) {
    return new Date(new Date(+this).setSeconds(this.getSeconds() + num));
};
Date.prototype.addMilliseconds = function (num) {
    return new Date(new Date(+this).setMilliseconds(this.getMilliseconds() + num));
};
Array.prototype.sum = function (selector) {
    if (this.length === 0)
        return 0;
    let isArrayOfObjects = typeof this[0] === 'object';
    if (isArrayOfObjects && selector == undefined)
        throw 'Missing a selector.';
    return isArrayOfObjects ? this.map(selector).reduce((a, b) => a + b, 0) : this.reduce((a, b) => a + b, 0);
};
Array.prototype.groupBy = function (keySelector, elementSelector) {
    let map = this.reduce((entryMap, curr) => {
        let key = keySelector(curr);
        let value = elementSelector ? elementSelector(curr) : curr;
        return entryMap.set(key, [...entryMap.get(key) || [], value]);
    }, new Map());
    return [...map.entries()].map(([key, array]) => Object.defineProperty(array, 'key', {value: key}));
};
