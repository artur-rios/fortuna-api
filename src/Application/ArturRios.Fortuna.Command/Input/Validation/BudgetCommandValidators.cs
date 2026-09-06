using ArturRios.Fortuna.Shared.Messages;
using FluentValidation;

namespace ArturRios.Fortuna.Command.Input.Validation;

public sealed class CreateBudgetCommandValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetCommandValidator()
    {
        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage(BudgetMessages.AmountMustBePositive);
        RuleFor(command => command.CurrencyCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(BudgetMessages.CurrencyRequired)
            .Length(3)
            .WithMessage(BudgetMessages.CurrencyInvalid);
        RuleFor(command => command.PeriodType)
            .IsInEnum()
            .WithMessage(BudgetMessages.PeriodTypeInvalid);
        RuleFor(command => command.PeriodStart)
            .NotEmpty()
            .WithMessage(BudgetMessages.PeriodStartRequired);
        RuleFor(command => command.CategoryIds)
            .NotEmpty()
            .WithMessage(BudgetMessages.CategoriesRequired);
        RuleForEach(command => command.CategoryIds)
            .NotEmpty()
            .WithMessage(BudgetMessages.CategoryIdInvalid);
    }
}

public sealed class UpdateBudgetCommandValidator : AbstractValidator<UpdateBudgetCommand>
{
    public UpdateBudgetCommandValidator()
    {
        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage(BudgetMessages.AmountMustBePositive);
        RuleFor(command => command.CurrencyCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(BudgetMessages.CurrencyRequired)
            .Length(3)
            .WithMessage(BudgetMessages.CurrencyInvalid);
        RuleFor(command => command.PeriodType)
            .IsInEnum()
            .WithMessage(BudgetMessages.PeriodTypeInvalid);
        RuleFor(command => command.PeriodStart)
            .NotEmpty()
            .WithMessage(BudgetMessages.PeriodStartRequired);
        RuleFor(command => command.CategoryIds)
            .NotEmpty()
            .WithMessage(BudgetMessages.CategoriesRequired);
        RuleForEach(command => command.CategoryIds)
            .NotEmpty()
            .WithMessage(BudgetMessages.CategoryIdInvalid);
    }
}
