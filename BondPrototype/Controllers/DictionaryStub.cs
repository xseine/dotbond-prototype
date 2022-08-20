using System.Collections;
using System.Linq.Expressions;

namespace BondPrototype.Controllers;

/// <summary>
/// Class used as the parameter in dynamic queries.
/// Property access is simulated using string indexing, where the return type is the class itself.
/// It contains implicit cast operators for usage in operations with integers, strings,...
///
/// IQueryable adds the "array" methods to the stub,
/// and receives parameters for those methods as stub Expressions, which must also be converted to correct type. 
/// </summary>
public class DictionaryStub : IQueryable<DictionaryStub>
{
    private const string ErrorMessage = "This class is used for expression tree analysis. It is not supposed to be instantiated.";
    
    public DictionaryStub() => throw new NotImplementedException(ErrorMessage);

    public DictionaryStub this[string _] => throw new NotImplementedException(ErrorMessage);

    
    // Casting
    public static implicit operator string(DictionaryStub _) => throw new NotImplementedException(ErrorMessage);
    public static implicit operator int(DictionaryStub _) => throw new NotImplementedException(ErrorMessage);
    public static implicit operator bool(DictionaryStub _) => throw new NotImplementedException(ErrorMessage);
    public static implicit operator DateTime(DictionaryStub _) => throw new NotImplementedException(ErrorMessage);
    
    
    // Operations
    public static DictionaryStub operator +(DictionaryStub _, DictionaryStub __) => throw new NotImplementedException(ErrorMessage);
    
    public IEnumerator<DictionaryStub> GetEnumerator()
    {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public Type ElementType { get; }
    public Expression Expression { get; }
    public IQueryProvider Provider { get; }
}