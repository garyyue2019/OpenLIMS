class SpecgenError(Exception):
    """Base error for expected specification compiler failures."""


class ConfigurationError(SpecgenError):
    """Raised when specgen.json is missing or invalid."""


class ValidationError(SpecgenError):
    """Raised when one or more specification invariants are violated."""

    def __init__(self, messages: list[str]):
        self.messages = messages
        super().__init__("\n".join(messages))


class DriftError(SpecgenError):
    """Raised when source or generated artifacts are stale."""
