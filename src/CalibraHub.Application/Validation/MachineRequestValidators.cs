using CalibraHub.Application.Contracts;
using FluentValidation;

namespace CalibraHub.Application.Validation;

public sealed class CreateMachineRequestValidator : AbstractValidator<CreateMachineRequest>
{
    public CreateMachineRequestValidator()
    {
        RuleFor(x => x.LocationId).GreaterThan(0).WithMessage("Lokasyon secimi zorunludur.");
        // Code UI'dan kaldirildi — backend MAC-{hex} olarak otomatik uretir (CLAUDE.md: kullanici kod girmez).

        // HourlyCapacity opsiyonel ama girilirse pozitif olmali
        When(x => x.HourlyCapacity.HasValue, () =>
        {
            RuleFor(x => x.HourlyCapacity!.Value)
                .GreaterThan(0m).WithMessage("Saatlik kapasite pozitif olmalidir.");
        });
    }
}

public sealed class UpdateMachineRequestValidator : AbstractValidator<UpdateMachineRequest>
{
    public UpdateMachineRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Makine ID zorunludur.");
        RuleFor(x => x.LocationId).GreaterThan(0).WithMessage("Lokasyon secimi zorunludur.");
        // Code UI'dan kaldirildi — backend mevcut kodu korur (CLAUDE.md: kullanici kod girmez).
    }
}

public sealed class CreateLocationRequestValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(x => x.LocationTypeCode)
            .NotEmpty().WithMessage("Lokasyon tipi zorunludur.")
            .MaximumLength(50);

        RuleFor(x => x.LocationCode)
            .NotEmpty().WithMessage("Lokasyon kodu zorunludur.")
            .MaximumLength(50);
    }
}
