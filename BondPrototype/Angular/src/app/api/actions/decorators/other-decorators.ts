
export function key(parameters?: any): (target: object | Function, propertyName: string) => any {
    return function(target: object | Function, propertyName: string) {
        addAttribute(target, propertyName, 'key', parameters);
    }
}
