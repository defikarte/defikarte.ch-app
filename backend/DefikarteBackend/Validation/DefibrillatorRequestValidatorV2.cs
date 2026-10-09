using DefikarteBackend.Model;
using FluentValidation;
using System.Text.RegularExpressions;

namespace DefikarteBackend.Validation
{
    public class DefibrillatorRequestValidatorV2 : AbstractValidator<DefibrillatorRequestV2>
    {
        public DefibrillatorRequestValidatorV2()
        {
            RuleFor(x => x.Latitude).NotEmpty();
            RuleFor(x => x.Longitude).NotEmpty();
            RuleFor(x => x.Reporter).NotEmpty();
            RuleFor(x => x.Location).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(200);
            RuleFor(x => x.OperatorPhone).Custom((value, context) => PhoneNumberValid(value, context)).When(x => !string.IsNullOrEmpty(x.OperatorPhone));
            RuleFor(x => x.Access).Custom((value, context) => AccessValid(value, context));
            RuleFor(x => x.Indoor).NotNull().NotEmpty().Custom((value, context) => IndoorValid(value, context));
            RuleFor(x => x.Level).Custom((value, context) => IsNumber(value, context));
            // opening hours validation is missing
        }

        private static void PhoneNumberValid(string? phoneNumberRaw, ValidationContext<DefibrillatorRequestV2> context)
        {
            if (string.IsNullOrEmpty(phoneNumberRaw))
            {
                return;
            }

            var result = Regex.Match(phoneNumberRaw, "112|144|117|118|1414");
            if (result.Success)
            {
                return;
            }

            var phoneNumberUtil = PhoneNumbers.PhoneNumberUtil.GetInstance();
            try
            {
                var value = phoneNumberUtil.Parse(phoneNumberRaw, "CH");
                var valid =
                  phoneNumberUtil.IsPossibleNumber(value) &&
                  phoneNumberUtil.IsValidNumber(value) &&
                 (phoneNumberUtil.IsValidNumberForRegion(value, "CH")
                 || phoneNumberUtil.IsValidNumberForRegion(value, "LI"));

                if (!valid)
                {
                    context.AddFailure(context.PropertyPath, "Phonenumber not valid");
                }
            }
            catch (Exception)
            {
                context.AddFailure(context.PropertyPath, "Phonenumber not valid");
            }
        }

        private static void AccessValid(string? access, ValidationContext<DefibrillatorRequestV2> context)
        {
            if (string.IsNullOrEmpty(access))
            {
                return;
            }

            if (!(new List<string> { "yes", "no", "private", "permissive" }).Contains(access))
            {
                context.AddFailure(context.PropertyPath, "Access not valid");
            }
        }

        private static void IndoorValid(string? indoor, ValidationContext<DefibrillatorRequestV2> context)
        {
            if (string.IsNullOrEmpty(indoor))
            {
                return;
            }

            if (!(new List<string> { "yes", "no" }).Contains(indoor))
            {
                context.AddFailure(context.PropertyPath, "Indoor not valid");
            }
        }

        private static void IsNumber(string? value, ValidationContext<DefibrillatorRequestV2> context)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (!int.TryParse(value, out _))
            {
                context.AddFailure(context.PropertyPath, "Value is not a number");
            }
        }
    }
}