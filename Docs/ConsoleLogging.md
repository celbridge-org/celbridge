# Console Logging

The console log uses text color to indicate the type of each log message (Info, Ok, Error, Warn).

Currently, the type is determined by prepending the type in the message: "warn:You are on fire".
This is mostly because we don't currently support enum properties for Instructions.

Once enum support is in place, this system should be replaced with a Log Type enum property.