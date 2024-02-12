# Automated Testing

Automated testing is critically important in Celbridge to ensure that changes made to the application source code
don't introduce bugs or break existing functionality. However...

# Unit Tests and Mocking suck

The typical way to ensure code quality in the software industry is primarily via adding a huge number of tiny Unit Tests for individual classes and methods, and perhaps some integration tests to test the behavior overall application.

This can be quite a divisive topic for developers, but my opinion of it is that the majority of unit testing and code coverage metrics are a wasteful form of cargo cult security theatre that adds little real value, and often incurs a very high development cost. 

This is the kind of thing overpaid consultants love, because they can charge by the hour and show the executives a shiny powerpoint graph with their code coverage metrics. It's not so good if you're goal is to actually deliver useful and robust software with a minimum of pointless busy work.

These articles sum up my thoughts on this subject pretty well:

https://tyrrrz.me/blog/unit-testing-is-overrated
https://tyrrrz.me/blog/fakes-over-mocks

# Functional Tests

The majority of tests in Celbridge are _functional_ tests that check the application behaviour from the client's perspective. Our tests favour using either the real dependency or a faked (not mocked) dependency as much as possible. 

In some cases this may resemble a simple unit test of a single method or class. In other cases, we instantiate multiple dependent classes and go through a complex setup phase to get the program into the desired state.

In all cases, the goal is to validate that some client facing aspect of the program is functioning correctly. Clients in this case can be either users of the Celbridge Application, or developers coding extensions that work with the Celbridge Core Library. 

Any test which doesn't validate behaviour that is relevant to a client needs to justify its existence, because every test we add incurs a maintenance overhead forever. 

# Fakes

To facilitate functional testing, and avoid the use of mocks, we use simplified Fake implementations of several interfaces. These support all the same interface methods as their "real" equivalents, but implement the functionality in a highly simplified manner.

These Fakes live in the same assembly as the tests themselves. They are only intended to support testing and shouldn't be used in production code.

# Test naming

We use a Behaviour Driven Design approach to naming tests. The test name should explain who the test is relevant to and what behaviour it's checking for. The name should not contain implementation details such as method / type names, how values or errors are returned, etc.

These example test methods from the articles linked above provide a good example of the style.

```
public class SolarTimesSpecs
{
    [Fact]
    public async Task User_can_get_solar_times_automatically_for_their_location() { /* ... */ }

    [Fact]
    public async Task User_can_get_solar_times_during_periods_of_midnight_sun() { /* ... */ }

    [Fact]
    public async Task User_can_get_solar_times_if_their_location_cannot_be_resolved() { /* ... */ }
}
```

Due to limitations in NUnity and the Visual Studio Test Explorer, our test names use pascal case, e.g.
```
UserCanGetSolarTimesAutomaticallyForTheirLocation()
```

