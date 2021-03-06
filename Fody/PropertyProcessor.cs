using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using EmptyStringGuard;

public class PropertyProcessor
{
    private const string CannotSetTheValueOfPropertyToEmptyString = "[EmptyStringGuard] Cannot set the value of property '{0}' to an empty string.";

    private readonly ValidationFlags _validationFlags;

    public PropertyProcessor(ValidationFlags validationFlags)
    {
        _validationFlags = validationFlags;
    }

    public void Process(PropertyDefinition property)
    {
        try
        {
            if (property.PropertyType.FullName != "System.String")
                return;
            if (property.IsGeneratedCode())
            {
                return;
            }
            InnerProcess(property);
        }
        catch (Exception exception)
        {
            throw new WeavingException($"An error occurred processing property '{property.FullName}'", exception);
        }
    }

    private void InnerProcess(PropertyDefinition property)
    {
        var localValidationFlags = _validationFlags;

        if (!property.PropertyType.IsRefType())
            return;

        var attribute = property.DeclaringType.GetEmptyStringGuardAttribute();
        if (attribute != null)
        {
            localValidationFlags = (ValidationFlags)attribute.ConstructorArguments[0].Value;
        }

        if (!localValidationFlags.HasFlag(ValidationFlags.Properties)) return;

        if (property.AllowsEmptyString())
            return;

        if (property.SetMethod != null && property.SetMethod.Body != null)
        {
            var setBody = property.SetMethod.Body;
            setBody.SimplifyMacros();

            if (localValidationFlags.HasFlag(ValidationFlags.NonPublic) || (property.SetMethod.IsPublic && property.DeclaringType.IsPublic))
            {
                InjectPropertySetterGuard(setBody, property.SetMethod.Parameters[0], String.Format(CultureInfo.InvariantCulture, CannotSetTheValueOfPropertyToEmptyString, property.FullName));
            }

            setBody.InitLocals = true;
            setBody.OptimizeMacros();
        }
    }

    private void InjectPropertySetterGuard(MethodBody setBody, ParameterDefinition valueParameter, string errorMessage)
    {
        if (!valueParameter.MayNotBeEmptyString())
            return;

        var guardInstructions = new List<Instruction>();

        var entry = setBody.Instructions.First();

        InstructionPatterns.LoadArgumentOntoStack(guardInstructions, valueParameter);

        InstructionPatterns.IfEmptyString(guardInstructions, entry, i =>
        {
            InstructionPatterns.LoadArgumentException(i, errorMessage, valueParameter.Name);

            // Throw the top item off the stack
            i.Add(Instruction.Create(OpCodes.Throw));
        });

        setBody.Instructions.Prepend(guardInstructions);
    }
}