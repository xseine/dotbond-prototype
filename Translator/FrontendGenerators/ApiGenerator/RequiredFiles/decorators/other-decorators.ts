// @ts-nocheck
export type attributes = never;
let modelAttributes: { [modelName: string]: { [propertyName: string]: { attribute: attributes, parameters: any }[] } } = {};

// Adds the specified validator function to the list of validators of the property in the model.
function addAttribute(target: object | Function, propertyName: string, attribute: attributes, parameters: any): void {
    let modelName = target.constructor.name;

    modelAttributes[modelName] = modelAttributes[modelName] ?? {};
    if (Object.keys(modelAttributes[modelName]).includes(propertyName)) return;

    modelAttributes[modelName][propertyName] = modelAttributes[modelName][propertyName] ?? [];

    modelAttributes[modelName][propertyName].push({attribute, parameters})
}


/*========================== Below are decorator functions ==========================*/
