namespace BondPrototype.Common.ActionFilters;

/// <summary>
/// Used to define a list of properties DotBond can run queries on using the IQueryable return value of the action.
/// If the query uses properties not specified in this attribute,
/// IQueryable will be evaluated using the operators up to the first of those property
/// and the evaluation will continue using the created IEnumerable.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ManagedQueryAttribute : Attribute
{
    public string[] Properties { get; set; }
    
    public ManagedQueryAttribute(params string[] properties)
    {
        Properties = properties;
    }
}