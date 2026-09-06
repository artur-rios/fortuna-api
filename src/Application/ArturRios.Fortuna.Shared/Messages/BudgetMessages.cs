namespace ArturRios.Fortuna.Shared.Messages;

public static class BudgetMessages
{
    public const string CreatedSuccessfully = "Budget created successfully.";
    public const string UpdatedSuccessfully = "Budget updated successfully.";
    public const string DeletedSuccessfully = "Budget deleted successfully.";
    public const string RetrievedSuccessfully = "Budget retrieved successfully.";
    public const string ListedSuccessfully = "Budgets retrieved successfully.";
    public const string ConsumptionRetrievedSuccessfully =
        "Budget consumption retrieved successfully.";
    public const string PeriodPrecedesBudget =
        "The requested period precedes the budget's start.";
    public const string NotFound = "Budget not found.";
    public const string CategoryNotFound = "One or more categories were not found.";
    public const string CurrencyNotSupported = "The currency is not supported.";
    public const string ProfileNotFound = "The acting user's profile was not found.";
    public const string AmountMustBePositive = "Amount must be greater than zero.";
    public const string CurrencyRequired = "CurrencyCode is required.";
    public const string CurrencyInvalid = "CurrencyCode must contain three characters.";
    public const string PeriodTypeInvalid = "PeriodType is invalid.";
    public const string PeriodStartRequired = "PeriodStart is required.";
    public const string CategoriesRequired = "At least one category is required.";
    public const string CategoryIdInvalid = "Category identifiers cannot be empty.";
}
