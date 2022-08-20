// @ts-nocheck
import {FormControl, FormGroup, ValidatorFn, Validators} from "@angular/forms";


let modelValidators: { [modelName: string]: { [propertyName: string]: ValidatorFn[] } } = {};

type generatedModels = 'EmploymentApplication';

/**
 * Adds model validation to the form group.
 * Validation is based on the typescript decorators that are generated with the model.
 * @param formGroup
 * @param modelName
 */
export function addValidation(formGroup: FormGroup, modelName: generatedModels): void {
    let modelValidation = modelValidators[modelName];
    if (!modelValidation) return;

    for (let propertyName in modelValidation) {
        let propertyValidation = modelValidation[propertyName];
        let abstractControl = formGroup.get(propertyName);

        if (!propertyValidation) return;
        if (!(abstractControl instanceof FormControl)) return;

        abstractControl.addValidators(propertyValidation);
    }
}

/**
 * Specifies that a data field value is required.
 */
export function required(): (target: object | Function, propertyName: string) => any {
    return function (target: object | Function, propertyName: string) {
        addValidator(target, propertyName, Validators.required);
    }
}

/**
 * Validates an email address.
 */
export function emailAddress(): (target: object | Function, propertyName: string) => any {
    return function (target: object | Function, propertyName: string) {
        addValidator(target, propertyName, Validators.email);
    }
}

/**
 * Specifies that a data field value must match the specified regular expression.
 */
export function regex(pattern: string): (target: object | Function, propertyName: string) => any {
    return function (target: object | Function, propertyName: string) {
        addValidator(target, propertyName, Validators.pattern(pattern));
    }
}

/**
 * Specifies the numeric range constraints for the value of a data field.
 */
export function range(minimum: number, maximum: number): (target: object | Function, propertyName: string) => any {
    return function (target: object | Function, propertyName: string) {
        addValidator(target, propertyName, Validators.min(minimum), Validators.max(maximum));
    }
}

/**
 * Specifies the minimum and maximum length of characters that are allowed in a data field.
 */
export function stringLength(maximum: number, {MinimumLength: minimum}: { MinimumLength?: number } = {}): (target: object | Function, propertyName: string) => any {
    return function (target: object | Function, propertyName: string) {
        addValidator(target, propertyName, Validators.maxLength(maximum));
        if (minimum)
            addValidator(target, propertyName, Validators.minLength(minimum));
    }
}

/**
 * Provides URL validation.
 */
export function url(): (target: object | Function, propertyName: string) => any {
    return function (target: object | Function, propertyName: string) {
        addValidator(target, propertyName, Validators.pattern(
            /(https?:\/\/(?:www\.|(?!www))[a-zA-Z0-9][a-zA-Z0-9-]+[a-zA-Z0-9]\.[^\s]{2,}|www\.[a-zA-Z0-9][a-zA-Z0-9-]+[a-zA-Z0-9]\.[^\s]{2,}|https?:\/\/(?:www\.|(?!www))[a-zA-Z0-9]+\.[^\s]{2,}|www\.[a-zA-Z0-9]+\.[^\s]{2,})/));
    }
}


// Adds the specified validator function to the list of validators of the property in the model.
function addValidator(target: object | Function, propertyName: string, ...validators: ValidatorFn[]): void {
    let modelName = target.constructor.name;

    modelValidators[modelName] = modelValidators[modelName] ?? {};
    if (Object.keys(modelValidators[modelName]).includes(propertyName)) return;

    modelValidators[modelName][propertyName] = modelValidators[modelName][propertyName] ?? [];

    modelValidators[modelName][propertyName].push(...validators)
}
