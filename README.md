# HotStatus
Visual Studio extension to display error messages on the status bar

![image](https://user-images.githubusercontent.com/17131343/105683067-f2242c00-5f3e-11eb-9e05-0fa748ee0d1d.png)

## Why use Hot Status?

I see there are errors and warnings on my page.
What is that error??

### Three options:

#### Option1: Error List
Error List hard to match with errors on page.
Which message lines up with which squigly??
Lose your place really easily. :-o

*Better to stay on the page.*

#### Option 2: Use the Mouse (Yuk!)
Can hover the mouse cursor over the area

I don’t want to use the mouse!
- It’s too slow
- It’s really fiddly!
- Info disappears easily

#### Option 3: Keyboard shortcut
Show Quick Info: Ctrl+K, Ctrl+I
- Double-key shortcut combo. :-o
- Requires 2 hands :-(

*Urghh… so painful.*

I want the information available immediately
- readily available
- constantly available

## Introducing Hot Status

Tools → Options

Hot Settings → Hot Status

**[New option!]**

Show Error Info

“Show Error Info on status bar”

When caret is on an Error, Error Info is displayed on the Status Bar

Will show, Warnings and Info, too!
(Shows most severe issue)

Alt+PgDn = Go to Next Error/Issue
Alt+PgUp = Previous Error/Issue

Easily scan through errors and issues
Info available at a glance
keyboard focus in place
ready to apply fixes (Alt+Enter)
No need for a mouse!

Want more information?

What type is that var?
What does this method return?
What are the parameters?

**[Additional option!]**
Show Symbol Info
“Show Symbol Info on the status bar”

When no error/issue...
Shows QuickInfo for current symbol
(Relevant info about the item)

Really useful when:
- Using implicitly-typed vars (ie. “var”)
- Variable declaration not visible
- Methods with ambiguous return types

Faster and easier than
- Hovering mouse, or
- Using Ctrl+K, Ctrl+I shortcut

**Need even more info?**

When in doubt, press Ctrl+K, Ctrl+I (Show Quick Info)
- Full color dialog with all relevant info


## Install the Tool

Extensions → Manage **Extensions**

Search (Ctrl+E)

![image](https://user-images.githubusercontent.com/17131343/105684140-4c71bc80-5f40-11eb-8e6a-f6e2b2da5c5d.png)

**Hot Status**
By Justin Clareburt

Available on the [**Visual Studio Marketplace**](
https://marketplace.visualstudio.com/items?itemName=JustinClareburtMSFT.HotStatus)

Search for “Hot Status”
![image](https://user-images.githubusercontent.com/17131343/105684413-a83c4580-5f40-11eb-9d66-da44361c17dd.png)
