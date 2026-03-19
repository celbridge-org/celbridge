# Markdown Preview Test Document

This document tests all GFM (GitHub Flavored Markdown) features supported by the Celbridge markdown preview.

---

## Basic Formatting

**Bold text** and *italic text* and ***bold italic text***

~~Strikethrough text~~

`Inline code` with backticks

---

## Headings

# Heading 1
## Heading 2
### Heading 3
#### Heading 4
##### Heading 5
###### Heading 6

---

## Links

### External Links
- [GitHub](https://github.com)
- [Google](https://google.com "Google Homepage")
- Autolink: https://example.com

### Local Resource Links
- [Link to sibling file](sibling.md)
- [Link to file in subfolder](subfolder/document.md)
- [Link to parent folder](../other-folder/file.txt)

### Anchor Links
- [Jump to Tables Section](#tables)
- [Jump to Code Blocks](#code-blocks)

### Email Links
- [Send email](mailto:test@example.com)

---

## Images

### External Image
![Placeholder Image](https://via.placeholder.com/150 "Placeholder")

### Local Image (relative to this file)
![Local Image](images/test-image.png)

### Image in subfolder
![Subfolder Image](assets/images/photo.jpg "A photo")

---

## Lists

### Unordered Lists
- Item 1
- Item 2
  - Nested item 2.1
  - Nested item 2.2
    - Deeply nested 2.2.1
- Item 3

### Ordered Lists
1. First item
2. Second item
   1. Nested ordered 2.1
   2. Nested ordered 2.2
3. Third item

### Mixed Lists
1. Ordered item
   - Unordered nested
   - Another unordered
2. Another ordered
   1. Nested ordered
      - Deep unordered

---

## Task Lists

### Simple Tasks
- [ ] Unchecked task
- [x] Checked task
- [ ] Another unchecked task

### Nested Tasks
- [ ] Parent task
  - [ ] Child task 1
  - [x] Child task 2 (completed)
  - [ ] Child task 3
- [x] Completed parent task
  - [x] All children done
  - [x] This one too

### Tasks with Formatting
- [ ] Task with **bold** text
- [x] Task with `inline code`
- [ ] Task with [a link](https://example.com)

---

## Tables

| Column 1 | Column 2 | Column 3 |
|----------|----------|----------|
| Row 1 A  | Row 1 B  | Row 1 C  |
| Row 2 A  | Row 2 B  | Row 2 C  |
| Row 3 A  | Row 3 B  | Row 3 C  |

### Table with Alignment

| Left Aligned | Center Aligned | Right Aligned |
|:-------------|:--------------:|--------------:|
| Left         | Center         | Right         |
| Text         | Text           | Text          |
| More         | More           | More          |

### Table with Formatting

| Feature | Status | Notes |
|---------|--------|-------|
| **Bold** | `code` | *italic* |
| [Link](https://example.com) | ~~strike~~ | Normal |

---

## Code Blocks

### Inline Code
Use `console.log()` for debugging. The `Array.map()` method is useful.

### Fenced Code Blocks

```javascript
// JavaScript example
function greet(name) {
    console.log(`Hello, ${name}!`);
    return { greeting: `Hello, ${name}!` };
}

greet('World');
```

```python
# Python example
def factorial(n):
    if n <= 1:
        return 1
    return n * factorial(n - 1)

print(factorial(5))
```

```csharp
// C# example
public class Example
{
    public string Name { get; set; }
    
    public void Greet()
    {
        Console.WriteLine($"Hello, {Name}!");
    }
}
```

```html
<!-- HTML example -->
<!DOCTYPE html>
<html>
<head>
    <title>Test</title>
</head>
<body>
    <h1>Hello World</h1>
</body>
</html>
```

```
Plain code block without language
Just preformatted text
    With indentation preserved
```

---

## Blockquotes

> This is a simple blockquote.

> This is a multi-line blockquote.
> It continues on this line.
> And this line too.

> Nested blockquotes:
> > This is nested
> > > And even deeper

> Blockquote with formatting:
> - List item in quote
> - Another item
> 
> **Bold text** and `code` in a quote.

---

## Horizontal Rules

Three or more hyphens:

---

Three or more asterisks:

***

Three or more underscores:

___

---

## Special Characters & Escaping

### HTML Entities
- Less than: &lt;
- Greater than: &gt;
- Ampersand: &amp;
- Quote: &quot;

### Escaping Markdown
\*Not italic\*
\**Not bold\**
\`Not code\`
\# Not a heading

### Backticks in Code
`` `backticks` inside code ``
``` `` nested backticks `` ```

---

## Emojis (Unicode)

- Checkmark: ✓ ✔️
- Cross: ✗ ❌
- Stars: ⭐ ★ ☆
- Arrows: → ← ↑ ↓ ↔
- Warning: ⚠️
- Info: ℹ️
- Fire: 🔥
- Rocket: 🚀

---

## Edge Cases & Error Handling

### Malformed Tables
| Incomplete | Table
| Only | Two

### Deeply Nested Content
1. Level 1
   1. Level 2
      1. Level 3
         1. Level 4
            1. Level 5
               1. Level 6
                  - Mixed with unordered
                    - [ ] And task lists
                      - Even deeper

### Very Long Lines
This is a very long line that should wrap properly in the preview. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.

### Special URL Characters
- [URL with spaces](path/to/file%20with%20spaces.md)
- [URL with query](https://example.com/page?foo=bar&baz=qux)
- [URL with hash](https://example.com/page#section)

### Empty Elements
- 
- [ ] 
- **
- ``

### Mixed Formatting
***Bold and italic*** at the same time

~~**Strikethrough bold**~~

`code with **markdown** inside` (should render as literal)

---

## Performance Test

### Large List
1. Item 1
2. Item 2
3. Item 3
4. Item 4
5. Item 5
6. Item 6
7. Item 7
8. Item 8
9. Item 9
10. Item 10
11. Item 11
12. Item 12
13. Item 13
14. Item 14
15. Item 15
16. Item 16
17. Item 17
18. Item 18
19. Item 19
20. Item 20

---

## End of Test Document

If you can see this, the preview successfully rendered the entire document! 🎉

---

## Unclosed Code Block Test (MUST BE LAST)

This test must be at the very end because an unclosed code block will consume all content that follows it.

```javascript
// This code block is intentionally not closed
// Everything after this will be treated as code content
function broken() {
    console.log("unclosed");
