using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace FlyVibeMex.Services;

/// <summary>
/// Mensajes de validación en español para lo que no trae su propio ErrorMessage (T6.1):
/// <list type="bullet">
/// <item>Los del model binding ("The value '' is invalid", "The field X must be a number"): <see cref="Configurar"/>.</item>
/// <item>Los de DataAnnotations sin ErrorMessage, incluido el [Required] implícito que agrega ASP.NET a los tipos no
/// anulables: <see cref="IValidationMetadataProvider"/>. El {0} es el [Display(Name)] del campo.</item>
/// </list>
/// Los mismos textos llegan al navegador en los atributos data-val-*. Se registra en Program.cs.
/// </summary>
public sealed class MensajesValidacionEspanol : IValidationMetadataProvider
{
    /// <summary>Traduce los mensajes del model binding (cuando el texto escrito no se puede convertir al tipo).</summary>
    public static void Configurar(MvcOptions opciones)
    {
        var m = opciones.ModelBindingMessageProvider;
        m.SetAttemptedValueIsInvalidAccessor((valor, campo) => $"«{valor}» no es un valor válido para {campo}.");
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(valor => $"«{valor}» no es un valor válido.");
        m.SetUnknownValueIsInvalidAccessor(campo => $"El valor de {campo} no es válido.");
        m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor no es válido.");
        m.SetValueIsInvalidAccessor(valor => $"«{valor}» no es válido.");
        m.SetValueMustBeANumberAccessor(campo => $"{campo} debe ser un número.");
        m.SetNonPropertyValueMustBeANumberAccessor(() => "Debe ser un número.");
        m.SetValueMustNotBeNullAccessor(campo => $"{campo} es obligatorio.");
        m.SetMissingBindRequiredValueAccessor(campo => $"Falta el valor de {campo}.");
        m.SetMissingKeyOrValueAccessor(() => "Falta un valor obligatorio.");
        m.SetMissingRequestBodyRequiredValueAccessor(() => "No se recibieron datos.");

        opciones.ModelMetadataDetailsProviders.Add(new MensajesValidacionEspanol());
    }

    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        foreach (var atributo in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
        {
            // Solo si el programador no puso su propio mensaje.
            if (!string.IsNullOrEmpty(atributo.ErrorMessage) || atributo.ErrorMessageResourceType is not null) continue;

            atributo.ErrorMessage = atributo switch
            {
                RequiredAttribute => "{0} es obligatorio.",
                StringLengthAttribute { MinimumLength: > 0 } => "{0} debe tener entre {2} y {1} caracteres.",
                StringLengthAttribute => "{0} puede tener máximo {1} caracteres.",
                MaxLengthAttribute => "{0} puede tener máximo {1} caracteres.",
                MinLengthAttribute => "{0} debe tener al menos {1} caracteres.",
                RangeAttribute => "{0} debe estar entre {1} y {2}.",
                EmailAddressAttribute => "{0} no es un correo válido.",
                PhoneAttribute => "{0} no es un teléfono válido.",
                UrlAttribute => "{0} no es una dirección web válida.",
                RegularExpressionAttribute => "{0} no tiene el formato correcto.",
                CompareAttribute => "{0} no coincide.",
                _ => atributo.ErrorMessage
            };
        }
    }
}
