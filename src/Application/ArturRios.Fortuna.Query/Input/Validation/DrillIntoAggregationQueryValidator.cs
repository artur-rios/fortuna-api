using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Query.Input.Validation;

public sealed class DrillIntoAggregationQueryValidator : AbstractValidator<DrillIntoAggregationQuery>
{
    public DrillIntoAggregationQueryValidator()
    {
        RuleFor(query => query.Key)
            .NotEmpty()
            .WithMessage(TransactionDrillDownMessages.KeyRequired)
            .MaximumLength(8192)
            .WithMessage(TransactionDrillDownMessages.KeyTooLong);
        RuleFor(query => query.Dimension)
            .Must(dimension => string.IsNullOrWhiteSpace(dimension) ||
                AggregateTransactionsQueryValidator.SupportedDimensions.Contains(
                    dimension.Trim(),
                    StringComparer.OrdinalIgnoreCase))
            .WithMessage(query => TransactionDrillDownMessages.UnknownDimension(
                query.Dimension ?? string.Empty,
                AggregateTransactionsQueryValidator.SupportedDimensions));
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage(TransactionDrillDownMessages.InvalidPageNumber);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage(TransactionDrillDownMessages.InvalidPageSize);
    }
}
