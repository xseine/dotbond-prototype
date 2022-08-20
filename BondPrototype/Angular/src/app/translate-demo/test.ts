class Person {
    public name: string;
    public someCode: any;
}

let maybe = new Person();


// if (maybe is int)
// {
//     Console.WriteLine($"The nullable int 'maybe' has the value {maybe}");
// }
// else if (maybe is Person)
// {
//     Console.WriteLine("Basic IsExpression");
// }
// else if (maybe is Person person)
// {
//     Console.WriteLine($"The Person 'maybe' has the name {person.Name}");
// }
// else
if (maybe?.constructor?.name === 'Person' && (maybe as Person).name == "" && (maybe as Person).name == "Jack" && (maybe as Person).someCode?.constructor?.name === 'Person') {
    console.log(`The Person 'maybe' has the name ${somePerson.name}`);
} else if (maybe?.constructor?.name === 'Person' && typeof (maybe as Person).name === 'string') {
    console.log((maybe as Person).name);
} else if (maybe?.constructor?.name === 'Person' && (maybe as Person).someCode?.constructor?.name === 'Person') {
    console.log((maybe as Person));
}
// for later
// else if (maybe is {})
// {
//     
// }
else {
    console.log("The nullable int 'maybe' doesn't hold a value");
}


// string? message = "This is not the null string";
//
// if (message is not null)
// {
//     Console.WriteLine(message);
// }