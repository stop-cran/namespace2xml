namespace YamlCommentSpike;

internal static class CorpusExpectations
{
    private static readonly IReadOnlyList<CommentExpectation> All =
    [
        C("01-leading.yaml", "C01", true, CommentAssociationKind.Leading, "$.second"),
        C("01-leading.yaml", "C02", true, CommentAssociationKind.Leading, "$.nested.second"),
        C("01-leading.yaml", "C03", true, CommentAssociationKind.Leading, "$.items[1]"),

        C("02-inline.yaml", "C04", true, CommentAssociationKind.Inline, "$.scalar"),
        C("02-inline.yaml", "C05", true, CommentAssociationKind.Inline, "$.block"),
        C("02-inline.yaml", "C06", true, CommentAssociationKind.Inline, "$.items[0]"),
        C("02-inline.yaml", "C07", true, CommentAssociationKind.Inline, "$.items[1]"),

        C("03-comment-blocks.yaml", "C08", true, CommentAssociationKind.Leading, "$.after"),
        C("03-comment-blocks.yaml", "C09", true, CommentAssociationKind.Leading, "$.after"),
        C("03-comment-blocks.yaml", "C10", true, CommentAssociationKind.Leading, "$.spaced_after"),

        C("04-document-edges.yaml", "C11", true, CommentAssociationKind.DocumentLeading),
        C("04-document-edges.yaml", "C12", true, CommentAssociationKind.DocumentTrailing),

        C("05-nested-boundary.yaml", "C13", true, CommentAssociationKind.Leading, "$.next"),
        C("05-nested-boundary.yaml", "C14", true, CommentAssociationKind.Trailing, "$.other.child"),

        C("06-flow.yaml", "C15", true, CommentAssociationKind.Inline, "$.flow_map.a"),
        C("06-flow.yaml", "C16", true, CommentAssociationKind.Inline, "$.flow_sequence[0]"),
        C("06-flow.yaml", "C17", true, CommentAssociationKind.Inline, "$.closed_map"),
        C("06-flow.yaml", "C18", true, CommentAssociationKind.Inline, "$.closed_sequence"),

        C("07-document-markers.yaml", "C19", true, CommentAssociationKind.RejectedSyntax),
        C("07-document-markers.yaml", "C20", true, CommentAssociationKind.RejectedSyntax),
        C("07-document-markers.yaml", "C21", true, CommentAssociationKind.RejectedSyntax),
        C("07-document-markers.yaml", "C22", true, CommentAssociationKind.RejectedSyntax),
        C("07-document-markers.yaml", "C23", true, CommentAssociationKind.RejectedSyntax),
        C("07-document-markers.yaml", "C24", false, CommentAssociationKind.RejectedSyntax),

        N("08-hash-scalars.yaml", "C25"),
        N("08-hash-scalars.yaml", "C26"),
        N("08-hash-scalars.yaml", "C27"),
        C("08-hash-scalars.yaml", "C28", true, CommentAssociationKind.Inline, "$.actual"),

        C("09-block-scalars.yaml", "C29", true, CommentAssociationKind.Inline, "$.literal"),
        N("09-block-scalars.yaml", "C30"),
        C("09-block-scalars.yaml", "C31", true, CommentAssociationKind.Leading, "$.folded"),
        C("09-block-scalars.yaml", "C32", true, CommentAssociationKind.Inline, "$.folded"),
        N("09-block-scalars.yaml", "C33"),
        C("09-block-scalars.yaml", "C34", true, CommentAssociationKind.Inline, "$.next"),

        C("10-anchors-aliases-tags.yaml", "C35", true, CommentAssociationKind.Inline, "$.anchored"),
        C("10-anchors-aliases-tags.yaml", "C36", true, CommentAssociationKind.Leading, "$.anchored.child"),
        C("10-anchors-aliases-tags.yaml", "C37", true, CommentAssociationKind.Inline, "$.alias"),
        C("10-anchors-aliases-tags.yaml", "C38", true, CommentAssociationKind.Inline, "$.tagged"),
        C("10-anchors-aliases-tags.yaml", "C39", true, CommentAssociationKind.Leading, "$.scalar_anchor"),

        C("11-empty-and-explicit.yaml", "C40", true, CommentAssociationKind.Inline, "$.empty"),
        C("11-empty-and-explicit.yaml", "C41", true, CommentAssociationKind.Inline, "$.explicit.key"),

        C("12-comment-only.yaml", "C42", true, CommentAssociationKind.DocumentOnly),
        C("12-comment-only.yaml", "C43", false, CommentAssociationKind.DocumentOnly),

        C("13-root-values.yaml", "C44", true, CommentAssociationKind.DocumentLeading),
        C("13-root-values.yaml", "C45", true, CommentAssociationKind.Inline, "$[1]"),
        C("13-root-values.yaml", "C46", true, CommentAssociationKind.DocumentTrailing),

        C("14-comments-between-key-and-value.yaml", "C47", true, CommentAssociationKind.Leading, "$.key.child"),
        C("14-comments-between-key-and-value.yaml", "C48", true, CommentAssociationKind.Leading, "$.sequence[0]"),

        C("15-compact-mapping.yaml", "C49", true, CommentAssociationKind.Inline, "$.items[0].key"),
        C("15-compact-mapping.yaml", "C50", true, CommentAssociationKind.Leading, "$.items[1].second"),

        C("16-edge-comment-blocks.yaml", "C51", true, CommentAssociationKind.DocumentLeading),
        C("16-edge-comment-blocks.yaml", "C52", true, CommentAssociationKind.DocumentLeading),
        C("16-edge-comment-blocks.yaml", "C53", true, CommentAssociationKind.DocumentTrailing),
        C("16-edge-comment-blocks.yaml", "C54", true, CommentAssociationKind.DocumentTrailing),

        C("17-root-scalar.yaml", "C55", true, CommentAssociationKind.DocumentLeading),
        C("17-root-scalar.yaml", "C56", true, CommentAssociationKind.Inline, "$"),
        C("17-root-scalar.yaml", "C57", true, CommentAssociationKind.DocumentTrailing),

        C("18-flow-leading.yaml", "C58", true, CommentAssociationKind.Leading, "$.mapping.first"),
        C("18-flow-leading.yaml", "C59", true, CommentAssociationKind.Leading, "$.mapping.second"),
        C("18-flow-leading.yaml", "C60", true, CommentAssociationKind.Leading, "$.sequence[0]"),
        C("18-flow-leading.yaml", "C61", true, CommentAssociationKind.Leading, "$.sequence[1]"),

        C("19-chomping-indicators.yaml", "C62", true, CommentAssociationKind.Inline, "$.literal_keep"),
        C("19-chomping-indicators.yaml", "C63", true, CommentAssociationKind.Inline, "$.literal_indent"),
        C("19-chomping-indicators.yaml", "C64", true, CommentAssociationKind.Inline, "$.folded_keep"),

        C("20-trailing-nested-at-eof.yaml", "C65", true, CommentAssociationKind.Trailing, "$.parent.first"),
        C("21-root-flow-and-empty-item.yaml", "C66", true, CommentAssociationKind.Inline, "$"),
        C("22-empty-sequence-item.yaml", "C67", true, CommentAssociationKind.Inline, "$.items[0]"),
        C("23-root-block-scalar.yaml", "C68", true, CommentAssociationKind.Inline, "$"),
        C("23-root-block-scalar.yaml", "C69", true, CommentAssociationKind.DocumentTrailing),

        N("24-multiline-quoted.yaml", "C70"),
        C("24-multiline-quoted.yaml", "C71", true, CommentAssociationKind.Inline, "$.double_quoted"),
        N("24-multiline-quoted.yaml", "C72"),
        C("24-multiline-quoted.yaml", "C73", true, CommentAssociationKind.Inline, "$.single_quoted"),
        N("24-multiline-quoted.yaml", "C74"),
        C("24-multiline-quoted.yaml", "C75", true, CommentAssociationKind.Inline, "$.flow"),

        C("25-crlf.yaml", "C76", true, CommentAssociationKind.Inline, "$.first"),
        C("25-crlf.yaml", "C77", true, CommentAssociationKind.Leading, "$.second"),

        C("26-no-final-newline.yaml", "C78", true, CommentAssociationKind.DocumentTrailing),

        C("27-indentation-variants.yaml", "C79", true, CommentAssociationKind.Leading, "$.outer.child"),
        C("27-indentation-variants.yaml", "C80", true, CommentAssociationKind.Trailing, "$.other.child"),

        C("28-comments-before-indented-scalars.yaml", "C81", true, CommentAssociationKind.Leading, "$.mapping_scalar"),
        C("28-comments-before-indented-scalars.yaml", "C82", true, CommentAssociationKind.Leading, "$.sequence[0]"),
    ];

    public static IReadOnlyList<CommentExpectation> ForFile(string fileName) =>
        All.Where(expectation => string.Equals(
                expectation.FileName,
                fileName,
                StringComparison.Ordinal))
            .ToArray();

    private static CommentExpectation C(
        string fileName,
        string id,
        bool parserReported,
        CommentAssociationKind kind,
        string? ownerPath = null) =>
        new(fileName, id, true, parserReported, kind, ownerPath);

    private static CommentExpectation N(string fileName, string id) =>
        new(fileName, id, false, false, CommentAssociationKind.Unresolved, null);
}

internal sealed record CommentExpectation(
    string FileName,
    string Id,
    bool IsComment,
    bool ParserReported,
    CommentAssociationKind Kind,
    string? OwnerPath);
