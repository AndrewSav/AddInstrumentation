Quick and dirty way to add instrumentation to an assembly when you do not have the source code.
-----------------------------------------------------------------------------------------------

I'm recommending to use in in conjuction with [CodeInject](https://github.com/punitganshani/CodeInject)

This project uses [dnlib](https://github.com/0xd4d/dnlib) for IL re-writing.

Sometimes you can debug code even without sources with Reflector extensions or with mdbg, or with sosex plugin to windbg.

But for Unity 3D game this is not an option (or I was not able to figure out how, if you think this is wrong, please let 
me know), since Unity hosts Mono entirely inside a native executable.

So when I want to examine some variables in run-time or change some of the methods I'm using the following workflow.
Note, that if you just want to dump executed methods with parameters you can use CodeInject linked above on it's own,
but sometimes you want also to inspect some local variables or fields that are NOT parameters. Or you want to substitute
a method with your own by you do not want to write IL for it by hand. So here is what I do.

Let's assume the assembly with code I want to examine is Assembly-CSharp.dll. 

If I want to dump some variables form class `Game.Engine.Widget` in method `Process` I create a brand new assembly.
I reference Assembly-CSharp.dll from my new instrumentation assembly.
I create an attribute in there like this:

```
class InstrumentedAttribute : Attribute
{
}
```
I then decorate all the methods that I would like to port across from this new assembly to the source 
assembly (Assembly-CSharp.dll) with this attribute. Currently only methods are supported, not fields or properties,
simply because this is something that I've done in a couple of evenings. Also when re-writing the code from your new 
instrumentation assembly methods to the source one, I'm only supporting artifacts that I needed, such as fields, 
methods and generic methods. It is likely that something else won't work and you will need to update the code to
fix that.

I also create a method in every class that I created:
```
[Instrumented]
void LogValue(string name, object value)
{
}
```
This is an empty method because I'm relying on CodeInject to do the actual logging.

Then I can write the method that I'd like to substitute, inspect:
```
[Instrumented]
bool Process(IDataObject obj, float freq)
{
	// copy the reversed C# code here and modify it as needed
	// if the code uses this class fields / other methods you might want to add them as well
	// When you need to inspect a variable or a field do this
	LogValue("Time.unscaled", Time.unscaled);
	// The rest of your code goes here
}
```
Note that the instrumentation assembly is never going to run. It is only used so that instrumented methods 
could be copied from it to the source assmbly.

The I run this project:
```
AddInstrumentation -i instum.dll -s Assembly-CSharp.dll
```

It produces Assembly-CSharp.patched file next t to Assembly-CSharp.dll

After this I need to replace Assembly-CSharp.dll with Assembly-CSharp.patched in the target application folder
and run CodeInject on it.
