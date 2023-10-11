using System;

namespace Celbridge.Models
{
    public enum SyntaxState
    {
        Unknown,
        Valid,
        Error
    }

    public enum InstructionCategory
    {
        Text,
        Error,
        Comment,
        Identifier,             // MyVar, A, A1
        Operator,               // =, >, <=. etc.
        Separator,              // ,
        Number,
        Boolean,
        String,
        Parenthesis,
        Expression,             // (x + 5) * 2
        ControlFlow,            // If, For, While, etc.
        ControlTransfer,        // Continue, Break, Return, etc.
        Scope,                  // Begin, End
        Declaration,            // Let, Set
        FunctionCall,           // Print, DoStuff
    }

    public enum IndentModifier
    {
        NoChange,
        PreIncrement,
        PostIncrement,
        PreDecrement,
        PostDecrement,
        PreDecrementPostIncrement
    }
}
