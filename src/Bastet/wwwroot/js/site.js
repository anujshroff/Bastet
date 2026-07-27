// Subnet tree functionality
$(document).ready(function () {
    // Initialize all subnet children as visible (fully expanded)
    $('.subnet-children').show();
    
    // Update toggle icons
    updateToggleIcons();
    
    // Toggle subnet children visibility
    $('.subnet-toggle').on('click', function () {
        var $children = $(this).closest('.subnet-item').children('.subnet-children');

        // Update the icon when the animation finishes, not before it starts. jQuery applies
        // display:none for a hide only in the completion callback (a show sets display up front),
        // so reading :visible on the next statement reports the state we are animating away from
        // and paints "expanded" on a subtree that is closing. Nothing revisits it afterwards.
        $children.slideToggle(200, updateToggleIcons);
    });
    
    // Expand all subnets
    $('#expand-all').on('click', function () {
        $('.subnet-children').slideDown(200);
        updateToggleIcons();
    });
    
    // Collapse all subnets
    $('#collapse-all').on('click', function () {
        // Keep the first level visible
        $('.subnet-tree > .subnet-item > .subnet-children').show();
        // Both calls are needed. The callback corrects the deeper levels once they are actually
        // hidden; the bare call keeps the first level right even when the tree is only one level
        // deep, in which case the selector above matches nothing and the callback never fires.
        $('.subnet-tree .subnet-item .subnet-item > .subnet-children').slideUp(200, updateToggleIcons);
        updateToggleIcons();
    });
    
    // Function to update toggle icons
    function updateToggleIcons() {
        $('.subnet-toggle').each(function () {
            var $children = $(this).closest('.subnet-item').children('.subnet-children');

            // Leave childless subnets alone. The view omits .subnet-children entirely when there
            // are no children, and jQuery's :visible is false on an empty set - so without this the
            // else branch below repaints their flat dash as a collapsed expander that can never
            // expand, the startup loop having already unbound their click handler. Same test the
            // startup loop uses, so there is one definition of "leaf" rather than two that disagree.
            if ($children.children().length === 0) {
                return;
            }

            if ($children.is(':visible')) {
                $(this).html('<i class="bi bi-dash-square"></i>');
            } else {
                $(this).html('<i class="bi bi-plus-square"></i>');
            }
        });
    }
    
    // Only show toggle button if there are children
    $('.subnet-item').each(function () {
        var $children = $(this).children('.subnet-children');
        var $toggle = $(this).children('.subnet-toggle');
        
        if ($children.children().length === 0) {
            $toggle.html('<i class="bi bi-dash"></i>');
            $toggle.css('cursor', 'default');
            $toggle.off('click');
        }
    });
});
